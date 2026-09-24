# Matchbook design

Matchbook is the purchase-to-pay half of an ERP: a request to buy something, its approval against a budget, the
purchase order, the goods arriving, the supplier's invoice checked against both, and the payment. Five services
own one part each, talk only through integration events on RabbitMQ, and each keeps its own Postgres database.

This document is the contract the services are built to. The code is the authority on details; this is the
authority on who owns what and which rules hold across services.

## The flow

```
requester        Requisitions ──RequisitionSubmitted──▶ Budgets   reserve funds (pre-encumbrance)
                 Requisitions ◀──FundsReserved───────── Budgets
approvers        Requisitions: manager, then finance, then CFO, by amount
                 Requisitions ──RequisitionApproved───▶ Purchasing  draft purchase order
buyer            Purchasing ──PurchaseOrderCommitmentRequested──▶ Budgets  reservation becomes commitment
                 Purchasing ◀──FundsCommitted─────────── Budgets
                 Purchasing ──PurchaseOrderIssued─────▶ Payables, Requisitions
receiver         Purchasing ──GoodsReceived───────────▶ Payables
AP clerk         Payables: invoice captured, three-way match (order, receipt, invoice)
                 Payables ──InvoiceMatched────────────▶ Budgets (commitment becomes actual), Purchasing
                 Purchasing ──PurchaseOrderClosed─────▶ Budgets (release what is left), Requisitions
treasurers       Payables: payment run drafted by one treasurer, released by another, ISO 20022 file
                 Payables ──InvoicePaid───────────────▶ (anyone who cares)
supplier admins  Suppliers ──SupplierChanged──────────▶ Requisitions, Purchasing, Payables
budget admins    Budgets ──CostCentreChanged──────────▶ Requisitions
```

Every arrow is an integration event from `src/Shared/Matchbook.Contracts`. No service calls another over HTTP.

## Services

| Service | Owns | Publishes | Consumes |
|---|---|---|---|
| Suppliers | Supplier master data, bank accounts under four-eyes control | `SupplierChanged` | nothing |
| Budgets | Cost centres, budgets per fiscal year, the ledger of reservations, commitments and actuals | `CostCentreChanged`, `FundsReserved`, `FundsReservationRejected`, `FundsCommitted`, `FundsCommitmentRejected` | `RequisitionSubmitted`, `RequisitionRejected`, `RequisitionCancelled`, `PurchaseOrderCommitmentRequested`, `InvoiceMatched`, `PurchaseOrderClosed` |
| Requisitions | Requisitions and their approval route | `RequisitionSubmitted`, `RequisitionApproved`, `RequisitionRejected`, `RequisitionCancelled` | `FundsReserved`, `FundsReservationRejected`, `CostCentreChanged`, `SupplierChanged`, `PurchaseOrderIssued`, `PurchaseOrderClosed` |
| Purchasing | Purchase orders and goods receipts | `PurchaseOrderCommitmentRequested`, `PurchaseOrderIssued`, `GoodsReceived`, `PurchaseOrderClosed` | `RequisitionApproved`, `FundsCommitted`, `FundsCommitmentRejected`, `SupplierChanged`, `InvoiceMatched` |
| Payables | Supplier invoices, the three-way match, payment runs | `InvoiceMatched`, `InvoicePaid` | `SupplierChanged`, `PurchaseOrderIssued`, `GoodsReceived`, `PurchaseOrderClosed` |

The gateway (YARP) is the only public entry point. Keycloak issues tokens.

## Rules every service follows

### Layers

Each service is four projects, and `tests/Matchbook.ArchitectureTests` checks the compiled references:

- **`Matchbook.<Service>.Domain`**: aggregates, value objects, rules. References only `Matchbook.SharedKernel`. No
  EF attributes, no JSON attributes, no async. Throws `BusinessRuleException` with a stable code when a rule says
  no. Takes the time as an argument (`DateTimeOffset now`), never reads a clock.
- **`Matchbook.<Service>.Application`**: every use case a command or a query (`ICommand<T>`, `IQuery<T>` from the
  shared kernel) with one handler, in `Features/<Area>/Commands/<UseCase>/` or `Features/<Area>/Queries/<UseCase>/`,
  and the handlers of other services' events in `IntegrationEvents/` (ADR 0008). An area is one of the path groups in
  the API table below. Queries and writes through `I<Service>Db`, an interface declared here that exposes the `DbSet`s and
  `SaveChangesAsync`, implemented by the DbContext in Infrastructure. That interface is the only abstraction over
  persistence: no repositories, generic or otherwise. Publishes through `IEventPublisher` from the shared kernel.
  May reference EF Core (provider neutral), the contracts, and logging and DI abstractions. Never Npgsql,
  MassTransit or ASP.NET Core. `TimeProvider` for the clock.
- **`Matchbook.<Service>.Infrastructure`**: the DbContext and its configurations, migrations, MassTransit consumers
  (thin adapters that call an Application handler), service registration.
- **`Matchbook.<Service>.Api`**: minimal API endpoints in `Features/<Area>/`, the same areas as Application,
  authorization policies, `Program.cs`. Endpoints translate HTTP to a command or query and back, nothing more.

No project of one service references a project of another. What one service knows about another comes in events.

### Persistence

- Postgres 18, one database per service, owned by its own role. EF Core 10 with snake_case names.
- Migrations live in Infrastructure and run on start (a production deployment would run them as a job; noted in
  operations).
- Money is `numeric(18,2)`, quantities `numeric(18,3)`, unit prices `numeric(18,4)`. Always compute a line amount
  with `Amounts.Line(quantity, unitPrice)` so every service rounds identically.
- Aggregates that users edit concurrently carry a concurrency token (Postgres `xmin`). A lost race is a 409 with
  code `concurrency.conflict`, never a silent overwrite.
- Identifiers are `Guid.CreateVersion7()`. Human-facing numbers (`REQ-2026-000042`, `PO-2026-000017`) come from one
  Postgres sequence per service, with the document's year written into the number; they do not restart in January.
- Reads use `AsNoTracking`. Lists use keyset pagination (`?after=<cursor>&limit=`, limit capped at 200).

### Messaging

- MassTransit 8.5 on RabbitMQ with the Entity Framework outbox and inbox, configured once in `BuildingBlocks`.
  An event published from an API handler is written to the outbox table in the same `SaveChangesAsync` as the
  change that caused it. A consumer's publishes are held until it finishes and committed with its changes.
- The inbox drops a redelivered message within its deduplication window. **Consumers are idempotent anyway**,
  by natural keys in the database (a unique index on the document and step), because the window is finite and
  the check is cheap.
- **Any two events can arrive in either order.** RabbitMQ delivers each consumer's queue in order, but a service
  consumes several queues concurrently, and a retry reorders anything. Consumers must be correct under
  reordering:
  - State snapshots (`SupplierChanged`, `CostCentreChanged`) carry a `Version`; apply only if newer.
  - A consumer never throws because something it expects has not arrived yet. It stores the fact it received
    and acts when the rest is present. Payables stores a receipt for a purchase order it has not heard of, and
    matches when the order arrives.
  - A cancellation that arrives before the thing it cancels leaves a tombstone, so the late arrival is refused.
    Budgets records the release of a requisition's reservation even when there was nothing to release, and
    refuses to reserve for that requisition afterwards.
- Replies carry what they answer (`Attempt` on commitments), so a stale reply is recognisable.
- A consumer that fails is retried in memory with backoff, then parked on the `_error` queue. Business refusals
  are not failures: they are events (`FundsReservationRejected`), and the consumer succeeds.

### API

- Minimal APIs under the paths in the table below; the gateway routes `/api/<path>` to them.
- Every endpoint requires a token. Authorization by realm role (`Matchbook.SharedKernel.Roles`), and `auditor`
  may read everything and change nothing.
- Errors are RFC 9457 problem details with a `code` extension. `BusinessRuleException` maps to 422, 409, 404 or
  403 by its `Kind`. Validation of a request's shape is 400 with the failing fields.
- Every mutating endpoint returns the resource's new state, so a client never needs a second read.
- **Creates are idempotent on a client-supplied id.** A POST that creates something accepts an optional `id`
  (a version 7 GUID the client generates, since lists are ordered by id). Repeating the request with the same id returns what the first one created instead
  of creating a second; the same id with different content is a 409 `request.id_reused`. A client that lost the
  response to a timeout can retry without buying twice.
- A unique index the database enforces maps to a 409 with a stable code through `MapUniqueViolation`, so the
  loser of a race gets the same answer as a request the application refused itself.

| Service | Paths |
|---|---|
| Suppliers | `/suppliers` |
| Budgets | `/cost-centres`, `/budgets` |
| Requisitions | `/requisitions`, `/approvals` |
| Purchasing | `/purchase-orders` |
| Payables | `/invoices`, `/payment-runs` |

### Security

- Separation of duties is a domain rule with a test, not a UI convention. The rules are listed per service
  below.
- Four eyes: a bank account change, a supplier activation and a payment run each need a second person, and the
  second person cannot be the first.
- IBANs are encrypted at rest (AES-256-GCM, `ColumnProtector` in BuildingBlocks) in every service that stores
  one, and masked in every response except where a person has to verify them. They travel encrypted too:
  `SupplierChanged` carries the IBAN under the payment-data key that only Suppliers and Payables hold (ADR 0007),
  so it is never plain text in an outbox table or on the broker.
- No secret, token or full IBAN in a log line.

### Observability

OpenTelemetry traces (ASP.NET Core, HttpClient, Npgsql, MassTransit), metrics and logs over OTLP to the Aspire
dashboard in compose. Each service has a `Meter` named `Matchbook.<Service>` with counters for its business
outcomes (reservations granted and refused, matches by outcome, and so on).

### Tests

- Unit tests (`tests/Services/<Service>/Matchbook.<Service>.UnitTests`): the domain, exhaustively. Properties
  with FsCheck wherever a rule is arithmetic or ordering.
- Integration tests (`...IntegrationTests`): the running service against real Postgres and RabbitMQ in
  containers, through its HTTP API and real messages, using `tests/Matchbook.Testing`. No in-memory database and
  no in-memory transport: the claims are about the outbox, the inbox, constraints and concurrency, and a fake
  would prove only that the fake was configured.
- System tests (`tests/Matchbook.SystemTests`): the whole compose stack, end to end, then the reconciliation
  below.

## Suppliers

A supplier: legal name, tax id (unique after normalisation: upper case, letters and digits only), country
(ISO 3166 alpha-2), payment terms in days (0 to 120), a contact email, and a bank account.

- **Lifecycle**: `Draft` → `PendingActivation` → `Active` ⇄ `Blocked`. A `supplier-admin` creates and submits.
  A `supplier-approver` who is not the person who submitted activates. Blocking needs a reason; either role
  can block, only a `supplier-approver` unblocks.
- **Bank account**: proposed by a `supplier-admin` (IBAN checked by length per country and mod 97, BIC by
  format, account holder name), approved by a `supplier-approver` who did not propose it. Until approved, the
  previous verified account stays in force. Each approval increments `AccountVersion`. History is kept: who
  proposed, who approved, when. An activated supplier needs a verified account.
- **Publishes** `SupplierChanged` on activation, block, unblock, a newly verified account, or a change of name or
  terms, with a `Version` that rises by one each time.
- IBAN encrypted at rest, masked to the last four characters in responses, shown in full only to the
  `supplier-approver` reviewing a pending proposal.

## Budgets

A cost centre: code (`^[A-Z]{2,5}-[A-Z0-9]{2,12}$`), name, manager (a user id), active flag. A budget: one per
cost centre and fiscal year (the calendar year, UTC), with an allotted amount set by a `budget-admin`.

Every budget has four figures, and **available = allotted − reserved − committed − actual**:

- **reserved**: set aside for submitted requisitions not yet ordered.
- **committed**: purchase orders issued and not yet invoiced.
- **actual**: invoices matched.

Operations, each an entry in an append-only ledger with the balance updated in the same transaction:

| Trigger | Effect | Refused when |
|---|---|---|
| `RequisitionSubmitted` | reserve the amount | available is short, no budget, cost centre inactive, or the requisition was already released |
| `RequisitionRejected`, `RequisitionCancelled` | release the reservation, and leave a tombstone | never |
| `PurchaseOrderCommitmentRequested` | release the requisition's reservation and commit the order's amount, atomically | the order's amount exceeds available plus that reservation |
| `InvoiceMatched` | move `min(amount, the order's remaining commitment)` from committed to actual, and add any excess to actual | never: an invoice that passed the match is a fact |
| `PurchaseOrderClosed` | release the order's remaining commitment, or, if it was never committed, the requisition's reservation | never |

- **Nothing is ever reserved or committed beyond available**, under any concurrency. Two hundred simultaneous
  reservations against a budget with room for fifty grant exactly fifty. Use one conditional `UPDATE` per grant
  (or a row lock), not read-then-write.
- Each ledger entry is unique by (document, step), so a duplicate message changes nothing.
- The order of `InvoiceMatched` and `PurchaseOrderClosed` does not matter: both orders end with a commitment of
  zero and the same actual.
- An actual beyond the allotment (accepted price variances can do that) is allowed and reported as an overspend,
  never hidden.
- A `budget-admin` may raise an allotment, or lower it no further than what is consumed.

## Requisitions

A requisition: requester, cost centre, supplier, lines (1 to 50: description, quantity, unit of measure, unit
price estimate), a justification, a needed-by date. The amount is the sum of the line amounts.

- **Lifecycle**: `Draft` → `Submitted` → `PendingApproval` → `Approved` → `Ordered` → `Closed`, with
  `BudgetRejected`, `Rejected` and `Cancelled` as ends. Only its requester edits, submits or cancels it, and only
  before approval.
- On submit, the cost centre must be known and active and the supplier known and active, from the local copies
  kept from `CostCentreChanged` and `SupplierChanged`. A manager cannot raise a requisition against a cost centre
  they manage, since nobody could take the manager step.
- **Approval route**, fixed when funds are reserved: the cost centre's manager; then a `finance-approver` if the
  amount is over 10,000; then the `cfo` if over 100,000. Steps are sequential.
- **Separation of duties**: no one approves their own requisition, and no one approves two steps of the same
  requisition. The manager step can only be taken by the manager on record.
- `GET /approvals` lists what the calling user can act on now.
- A rejection needs a reason. `FundsReservationRejected` ends the requisition as `BudgetRejected`.
- `PurchaseOrderIssued` moves it to `Ordered`; `PurchaseOrderClosed` to `Closed`.
- A timeline per requisition: who did what, when.

## Purchasing

- `RequisitionApproved` drafts a purchase order (one per requisition, idempotent on the requisition) with the
  requisition's lines, number `PO-<year>-<sequence>`.
- A `buyer` may change quantities and prices on a draft (not below zero, not the supplier), then issue it. Issuing
  needs the supplier active in the local copy, and sends `PurchaseOrderCommitmentRequested` with the next
  `Attempt`; the order waits in `CommitmentPending`. `FundsCommitted` issues it and publishes
  `PurchaseOrderIssued`; `FundsCommitmentRejected` returns it to draft with the reason. A reply for an older
  attempt is ignored.
- A `receiver` records receipts against an issued order: per line, never more than ordered minus already
  received. **The receiver may not be the buyer who issued the order.**
- `InvoiceMatched` adds to each line's invoiced quantity. An order whose every line is fully received and fully
  invoiced closes as `Completed`.
- The buyer may short-close an issued order (`ShortClosed`) or cancel one with nothing received (`Cancelled`).
  Both publish `PurchaseOrderClosed`.

## Payables

An invoice: supplier, supplier invoice number, invoice date, the purchase order it bills, lines (order line
number, quantity, unit price), and the declared total, which must equal the sum of the line amounts.

- **Duplicates**: the invoice number is normalised (upper case, letters and digits only, leading zeros dropped
  from each run of digits, so `INV-0042` and `inv42` collide) and unique per supplier: a second capture is a 409
  `invoice.duplicate`. The same supplier, total and a date within seven days under a different number is a
  *suspected* duplicate, held for an `ap-approver`.
- **Three-way match**, per line, against the local copies of the order and its receipts:
  - quantity: everything invoiced on the line so far, this invoice included, may not exceed what was received.
    If it does, the invoice waits in `AwaitingReceipt` and is matched again automatically when goods arrive.
  - price: the variance on a line, `|invoiced − ordered| × quantity`, may not exceed 2% of the ordered line
    amount or 100.00, whichever is smaller. Beyond that it is a `PriceVariance` exception; an `ap-approver` who
    did not capture the invoice may accept it with a reason.
  - an invoice for an order not yet known waits in `AwaitingPurchaseOrder`; for a cancelled order, or a supplier
    other than the order's, it is rejected.
- A matched invoice publishes `InvoiceMatched` and becomes payable, due on invoice date plus the supplier's
  terms.
- **Payment runs**: a `treasurer` drafts a run for an execution date. It takes every payable invoice due by then
  whose supplier is active with a verified account, recording the account version it would pay. A different
  `treasurer` releases it. At release, an invoice whose supplier is no longer active, or whose account version
  changed since the draft, is dropped from the run and stays payable. Release marks the rest paid, publishes
  `InvoicePaid` for each, and produces an ISO 20022 `pain.001.001.09` credit transfer file (one transaction per
  invoice, remittance information carrying the supplier's invoice number), downloadable by treasurers.
- **An invoice is paid at most once**, under any concurrency: two runs drafted at the same moment never share an
  invoice, and releasing a run twice pays nothing twice.
- A draft run can be cancelled; its invoices become payable again.

## What must hold across services

After any sequence of requests, restarts and broker outages, once the queues are drained, the system tests check
across all five databases:

1. Every budget's figures equal the sum of its ledger, and replaying the ledger in order never takes available
   below zero on a reservation or commitment.
2. Reserved equals the amounts of requisitions submitted or approved and not yet ordered, closed or released.
3. Committed per order equals its amount less what was matched against it, floored at zero, while the order is
   open, and zero once closed.
4. Actual equals the sum of matched invoices for the budget's orders.
5. Every invoice is paid at most once, for exactly its matched amount, to the account version current at release.
6. No order line has more invoiced than received, or more received than ordered.

## Seeded identities

The Keycloak realm in `deploy/keycloak` has one user per role. Development only; every password is
`matchbook`.

| User | Id | Roles |
|---|---|---|
| rita | `a0000000-0000-4000-8000-000000000001` | requester |
| mark | `a0000000-0000-4000-8000-000000000002` | approver (manages ENG-PLATFORM) |
| maya | `a0000000-0000-4000-8000-000000000003` | approver (manages MKT-GROWTH) |
| fiona | `a0000000-0000-4000-8000-000000000004` | finance-approver |
| carl | `a0000000-0000-4000-8000-000000000005` | cfo |
| bruno | `a0000000-0000-4000-8000-000000000006` | buyer |
| rosa | `a0000000-0000-4000-8000-000000000007` | receiver |
| alice | `a0000000-0000-4000-8000-000000000008` | ap-clerk |
| aaron | `a0000000-0000-4000-8000-000000000009` | ap-approver |
| tess | `a0000000-0000-4000-8000-000000000010` | treasurer |
| trevor | `a0000000-0000-4000-8000-000000000011` | treasurer |
| sam | `a0000000-0000-4000-8000-000000000012` | supplier-admin |
| sofia | `a0000000-0000-4000-8000-000000000013` | supplier-approver |
| bob | `a0000000-0000-4000-8000-000000000014` | budget-admin |
| audrey | `a0000000-0000-4000-8000-000000000015` | auditor |
