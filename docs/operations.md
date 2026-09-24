# Operating Matchbook

For whoever runs the stack: what to configure, what to watch, what to do when it goes wrong, and what it does
not do yet. The commands assume the compose stack from the README; each one says what healthy output looks
like.

## What runs

| Container | Port on the host | Role |
|---|---|---|
| `gateway` | 5300 | The only public entry. Checks the token, limits each caller, routes `/api/*` to a service. |
| `suppliers`, `budgets`, `requisitions`, `purchasing`, `payables` | none | One service each, one database each, talking only through RabbitMQ. |
| `postgres` | 5440 | One server, five databases, one owner role per database (ADR 0005). |
| `rabbitmq` | 5672, 15672 | The broker, and its management UI and API. |
| `keycloak` | 8080 | Issues tokens. The realm in `deploy/keycloak` is for development only. |
| `dashboard` | 18888 | The Aspire dashboard: traces, metrics and logs from every service over OTLP. |

## Configuration

Every key is set as an environment variable in `compose.yaml` (double underscore for each level) and has a
development default in the service's `appsettings.json`. Every default below is a public development value.

| Key | Services | Compose value | What it does, and what changing it costs |
|---|---|---|---|
| `ConnectionStrings__Database` | all five | `Host=postgres;Database=<service>;Username=<service>;...` | The service's own database, as its own role. Pointing two services at one database breaks ADR 0005 silently: nothing stops it at startup. |
| `ConnectionStrings__RabbitMq` | all five | `amqp://matchbook:matchbook@rabbitmq:5672/` | The broker. Services start and take writes without it; events wait in the outbox until it answers. |
| `Auth__Authority` | all five, gateway | `http://keycloak:8080/realms/matchbook` | Where signing keys are fetched. Tokens must carry this issuer, so it has to match what Keycloak puts in `iss` (`KC_HOSTNAME`). |
| `Auth__Audience` | all five, gateway | `matchbook` | Tokens without this audience get 401. |
| `Auth__RequireHttpsMetadata` | all five, gateway | `false` | Must be `true` anywhere but a laptop. |
| `Encryption__ActiveKey` | Suppliers, Payables | `dev1` | The key new IBANs are encrypted under. See "Rotating the payment data key" before changing it. |
| `Encryption__Keys__<id>` | Suppliers, Payables | `dev1`: a base64 32 byte key | Every key that stored rows or queued messages may still use. Removing one that is still in use makes those IBANs unreadable, for good. |
| `Payer__Name`, `Payer__Iban`, `Payer__Bic` | Payables | a demo German account | The debtor in every pain.001 file. Checked at startup; a malformed IBAN stops the service. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | all five, gateway | `http://dashboard:18889` | Where traces, metrics and logs go. Unset, nothing is exported and nothing fails. |
| `ReverseProxy__Clusters__<service>__Destinations__primary__Address` | gateway | `http://<service>:8080` | Where the gateway sends each route. |
| `Cors__Origins` | gateway | unset | Browser origins allowed to call the API. Unset means none. |

These are constants in code rather than configuration, because changing them changes behaviour a test pins
down. Changing one is a release, not a deploy:

| Setting | Value | Where | Why this value |
|---|---|---|---|
| Consumer retry | 0.1, 0.5, 1, 3, 10 s, then the `_error` queue | `MessagingExtensions` | Long enough to ride out a database failover of a few seconds; short enough that a real bug parks within 15 s instead of blocking the queue. A `BusinessRuleException` is never retried: the same message would be refused the same way. |
| Outbox poll | every 100 ms | `MessagingExtensions` | The delay between a commit and its event leaving. Lower costs a query per service per tick. |
| Inbox deduplication window | 1 hour | `MessagingExtensions` | Consumers are idempotent by natural keys anyway, so this only saves work. |
| Concurrent messages per endpoint | 16, prefetch 32 | `MessagingExtensions` | Kept under the database pool; raising it moves the queue from RabbitMQ into Postgres lock waits. |
| Gateway limit per caller | 200 requests a second, bursts of 400 | `Matchbook.Gateway/Program.cs` | Per token subject, not per address, so an office behind one proxy is not one caller. |

## Health

Each service answers `/health/live` (the process is up, no checks) and `/health/ready` (its database and its
broker both answer). Compose uses `ready`, so `docker compose ps` shows a service as unhealthy while RabbitMQ is
down. It still accepts writes in that state: the events are in its outbox, and they leave when the broker is
back. The gateway has no dependencies to check, and compose asks it only for `live`.

```
docker compose ps --format "{{.Service}} {{.Status}}"
```

Healthy: every line says `(healthy)` except `dashboard`, which has no health check.

## What to watch

| Signal | Where | Alert when | Why |
|---|---|---|---|
| Messages on any `*_error` queue | RabbitMQ, `rabbitmqctl list_queues` | more than 0 | Each one is an event that failed six times, and the state it would have changed is now behind. A budget may hold money for a requisition that was rejected. |
| Rows in `outbox_message` | each service database | more than 1,000, or any row older than 5 minutes | Events are being written and not leaving: the broker is unreachable or the delivery service stopped. |
| Messages on a working queue | RabbitMQ | more than 1,000 for 5 minutes | A consumer is slow or stopped; its service's view of the others is going stale. |
| `matchbook.budgets.grant.duration`, p99 | dashboard | above 0.5 s for 5 minutes | Grants on one budget queue behind its row lock by design. A hot budget shows here before anything fails. |
| `matchbook.suppliers.refusals` with `code=supplier.self_approval` | dashboard | any | Someone tried to approve their own bank account change. Where a fraud review starts. |
| `matchbook.suppliers.changes` with `change=bank_account_approved` | dashboard | a jump against the usual daily count | Bank account changes are the classic payment fraud route. |
| `matchbook.budgets.overspends` | dashboard | any | An invoice took a budget past its allotment, which accepted price variances can do. Finance wants to know the same day. |
| 5xx from the gateway | dashboard, `http.server.request.duration` on `Matchbook.Gateway` | above 1% for 5 minutes | 502 and 503 mean a service is down behind the gateway. |
| 409 `concurrency.conflict` | dashboard, per service | a sustained rise | Two people keep changing one document at once, or one order is so busy that Payables' three internal retries are not enough. |

The outbox and error queue checks have no metric of their own yet. The queries:

```
docker compose exec postgres psql -U postgres -d budgets -c "select count(*), min(sent_time) from outbox_message"
docker compose exec rabbitmq rabbitmqctl list_queues name messages
```

Healthy: `count` 0 (or a handful, briefly), and `0` beside every queue.

## Runbook

### Messages are piling up on an `_error` queue

**Confirm.** `rabbitmqctl list_queues name messages` shows a count beside a name ending in `_error`, for
example `budgets-requisition-submitted_error`. Open it in the management UI (http://localhost:15672, user
`matchbook`) and read the `MT-Fault-Message` and `MT-Fault-StackTrace` headers of the first message.

**Act.** Fix the cause first: a message moved back before the fix fails six more times and returns. Then move
the messages back to the queue they came from. Consumers are idempotent, so a message that had partly
succeeded does no harm the second time.

```
docker compose exec rabbitmq rabbitmq-plugins enable rabbitmq_shovel rabbitmq_shovel_management
docker compose exec rabbitmq rabbitmqctl set_parameter shovel requeue '{"src-protocol":"amqp091","src-uri":"amqp://","src-queue":"budgets-requisition-submitted_error","src-delete-after":"queue-length","dest-protocol":"amqp091","dest-uri":"amqp://","dest-queue":"budgets-requisition-submitted"}'
```

**Verify.** The `_error` queue shows 0, the working queue drains, and the documents that were stuck move on (a
requisition leaves `Submitted`, an order leaves `CommitmentPending`).

**Do not purge the `_error` queue.** Every message on it is a money movement one service made and another has
not heard of. Purged, the two stay out of step for good: Budgets goes on holding a reservation for a
requisition Requisitions has already rejected, and nothing will ever release it.

### Requisitions stay in `Submitted`, or orders in `CommitmentPending`

Both mean Budgets has not answered.

**Confirm.** Is it the broker or Budgets? Check, in order:

1. `docker compose ps rabbitmq budgets`: both healthy?
2. The outbox query above against `requisitions` (or `purchasing`): rows waiting means the event never left.
3. `rabbitmqctl list_queues`: messages on `budgets-requisition-submitted` means Budgets is not consuming.
4. An `_error` queue for Budgets: see the section above.

**Act.** Bring back whichever is down: `docker compose start rabbitmq` or `docker compose start budgets`. Nothing
else is needed. Outboxes deliver on their own once the broker answers, and Budgets works through its queue on
start.

**Verify.** The outbox count falls to 0 and the documents move on within seconds. The chaos test measures
this: after a broker restart and Budgets killed mid-run, everything caught up within about a second of the
last request (`docs/measurements/chaos.md`).

**Do not delete rows from `outbox_message`.** They are the events. A deleted row is a reservation or a
commitment that one service recorded and no other will ever learn of.

### A service will not become ready

**Confirm.** `docker compose logs <service> --tail 50`. The usual causes: the database refuses the login (a
changed password), a migration failed, or `Payer__Iban` is malformed (Payables refuses to start rather than
write bad payment files).

**Act.** Fix the setting and `docker compose up -d <service>`. Migrations run at startup; Postgres runs DDL
inside a transaction, so a migration that fails leaves the schema as it was and the next start tries again.

**Verify.** `docker compose ps <service>` shows `(healthy)`.

### Rotating the payment data key

Suppliers encrypts each IBAN under the active key and sends it in `SupplierChanged` still encrypted; Payables
decrypts it with the same key (ADR 0007). Both services must therefore hold a key before either uses it.

1. Generate a key: `openssl rand -base64 32`.
2. Add it to both services as `Encryption__Keys__<new id>`, keeping every existing key and the current
   `Encryption__ActiveKey`. Deploy both.
3. Set `Encryption__ActiveKey` to the new id on both, and deploy. Order no longer matters: both already hold
   the key.
4. Keep the old key. Rows at rest in both databases, and any `SupplierChanged` still queued, are encrypted
   under it, and each stored value names its key (`v1.<key id>.<ciphertext>`).

**Verify.** Propose and approve a bank account on a test supplier, pay an invoice for it, and check the
`suppliers` database: the new `iban` values start with `v1.<new id>.`.

**Do not remove an old key** until every row that names it has been rewritten, which nothing does
automatically yet. A missing key makes those IBANs unreadable, and the error names the key it wanted.

## Known limitations

- **Migrations run when a service starts.** Fine for one replica. Several replicas starting together would race
  for the migration; production would run them as a separate job before the rollout.
- **Reconciliation runs only in the tests.** The eight invariants in `tests/Matchbook.Stack` are checked after
  every system test run, not on a schedule against a live system.
- **One currency** (ADR 0006). Every amount is in the company's currency; there is no FX.
- **No duplicate IBAN check across suppliers.** The column is encrypted under a random nonce, so it cannot be
  indexed. A keyed hash of the IBAN in its own column would allow it.
- **Old keys are never rewritten.** Key rotation works, but rows stay under the key they were written with
  until something rewrites them, and nothing does yet.
- **Only one replica per service has been tested.** The consumers are written to compete safely (unique
  indexes, row locks, `xmin`), but no test runs two replicas of a service against one queue.
- **Postgres is a single point of failure.** Every service needs its database to answer anything; the outbox
  covers a broker outage, not a database one.
- **The Keycloak realm is for development.** Every password is `matchbook`, and the `matchbook-cli` client
  allows the password grant so the `.http` files and the system tests can sign in without a browser.
