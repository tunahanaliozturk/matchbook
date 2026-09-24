# Budgets

Budgets owns cost centres, one budget per cost centre and fiscal year, and the ledger of everything that moved
money on a budget. It answers two questions for the rest of the system: may this requisition set money aside
(`FundsReserved` or `FundsReservationRejected`), and may this purchase order take its requisition's money over
(`FundsCommitted` or `FundsCommitmentRejected`). It publishes `CostCentreChanged` so Requisitions can route
approvals.

## Figures and ledger

A budget has four figures, and `available = allotted - reserved - committed - actual`. Every change to them is
a ledger entry carrying a movement on all four (`Movement`), so a budget's figures are the sum of its ledger,
including the allotment: opening a budget writes an `Open` entry and every allotment change an `Allot` entry,
with the budget admin who made it.

| Step | Document | Movement |
|---|---|---|
| `Open`, `Allot` | the budget, the change | allotted +x |
| `Reserve` | requisition | reserved +amount |
| `Release` | requisition | reserved -held |
| `Commit` | purchase order | reserved -held, committed +order amount |
| `Invoice` | invoice | committed -min(amount, remaining), actual +amount |
| `Close` | purchase order | committed -remaining |

An entry is unique by (document, step). Entries are only written for movements that move something, so
closing a fully invoiced order writes none.

Next to the ledger, each requisition and each purchase order has one row of state (`requisition_reservations`,
`order_commitments`): what is held, whether it was refused, released or committed, the last commitment attempt
decided, and what is left of an order's commitment. The row is created by whichever message about the document
arrives first. That is what the consumers decide on; the ledger is what they write.

## Staying correct under reordering and redelivery

The inbox drops most redeliveries. The handlers do not rely on it.

- **RequisitionSubmitted.** If the requisition already has a row, this is a redelivery and nothing happens: the
  first answer left in the same transaction as the change it answered. The one exception is a `Released` row,
  which means the release arrived first; the reservation is refused with `document_closed`. A refusal is
  recorded too, so a redelivered submission after funds were freed does not reserve for a requisition
  Requisitions has already ended as `BudgetRejected`, which nothing would ever release.
- **RequisitionRejected, RequisitionCancelled.** Release what is held. With no row yet, write a `Released` row
  with no budget: the tombstone. A second release finds nothing held.
- **PurchaseOrderCommitmentRequested.** An order that was ever committed answers `FundsCommitted` for any
  attempt, echoing it, and changes nothing. An order closed before it was committed is refused with
  `document_closed`. An attempt no newer than one already decided is stale and gets no answer: Purchasing has
  moved past it, and granting it would commit money for an order that is back in draft, possibly at an old
  price. Only a newer attempt is decided.
- **InvoiceMatched.** Skip it if the ledger already has its `Invoice` entry. Relief is
  `min(amount, remaining)`, the rest goes to actual.
- **PurchaseOrderClosed.** A closed order is left alone. A committed one releases what remains. One never
  committed releases its requisition's reservation, and its row becomes the tombstone a late commitment request
  is refused on.

Invoices and closes commute: whichever comes first, the commitment ends at zero and actual at the sum of the
invoices, because relief can never exceed what remains and closing takes whatever that is.

Two handlers racing over the same document both try to insert its row (primary key) or update it (`xmin`
token), so one of them fails, its transaction rolls back including its budget update, and the retry sees the
winner's row. The unique ledger key is the last line: even a path that slipped past every check cannot write a
second entry.

## Grants under concurrency

A reservation, a commitment and a lowering of the allotment are grants: they must not take available below
zero. Each is one statement:

```sql
UPDATE budgets
SET allotted = allotted + @a, reserved = reserved + @r, committed = committed + @c, actual = actual + @x
WHERE id = @id AND allotted - reserved - committed - actual >= @consumes
```

`@consumes` is what the entry takes from available: the amount for a reservation, the order amount less the
reservation it takes over for a commitment (so the design's "order amount at most available plus
reservation"), the reduction for an allotment. One row updated means granted. The handler then saves the
ledger entry and the document row in the same transaction.

Why this is enough: the UPDATE takes the row lock. A second grant on the same budget waits for the first to
commit, then Postgres re-reads the row as the first left it and evaluates the WHERE clause again (READ
COMMITTED). The check and the write cannot be split by another transaction, so two hundred concurrent
reservations against room for fifty grant fifty (`ConcurrencyTests`, with the numbers under Tests below). The
domain states the same inequality as `Budget.CanAfford`.

Alternatives considered:

- `SELECT ... FOR UPDATE`, decide in code, then `UPDATE`. Equally correct, but two round trips with the lock
  held across them, and the rule lives in two places.
- Load the budget, check, save with an `xmin` token. Correct, but under contention nearly every grant loses the
  race and retries, and a burst can exhaust the retry policy and park valid requests on the error queue.
- SERIALIZABLE. Same retry storm, and every other query in the transaction pays for it.

Every other movement (releases, invoices, closes, raises) is the same UPDATE without the condition. Budgets are
never loaded, edited and saved back, because that writes absolute figures computed from a row a grant may
already have changed. For the same reason `budgets` has no concurrency token: every reservation changes the
row, so a token would make every writer conflict with every other.

A side effect worth having: every ledger insert happens after its budget's row lock is taken, so within one
budget the ledger's identity sequence follows commit order, and a reader paging with `after=<sequence>` never
misses an entry that committed late.

## Database constraints

- `budgets`: each figure `>= 0`; `reserved + committed <= allotted` (grants never exceed available, invoices
  only move committed to actual, and an allotment is never lowered below consumption, so this always holds);
  fiscal year 2000 to 2100; unique (fiscal year, cost centre).
- `ledger_entries`: unique (document, step); `step` from the known set; index (budget, sequence) for the
  listing.
- `requisition_reservations`: `amount >= 0`; a `Held` row has a budget; `status` from the known set.
- `order_commitments`: `0 <= remaining <= amount`; a `Committed` row has a budget; `last_attempt >= 0`.
- `cost_centres`: the code pattern; `version >= 1`.
- `idempotent_requests`: the client's id is the primary key, so two creates racing with one id cannot both
  commit.

Available itself has no constraint: an invoice larger than its commitment is allowed to take it below zero,
and the floor grants respect is enforced by their UPDATE.

## Decisions the design left open

Made in the first phase, with the domain.

- **Refusals are remembered.** Alternative: re-decide a redelivered submission. Rejected, because the answer
  could change and leak a reservation (see above).
- **A tombstone is a document row, not a ledger entry.** `RequisitionCancelled` and `RequisitionRejected` carry
  no cost centre or year, so a release with nothing to release has no budget to be a ledger entry on. A ledger
  with a nullable budget would also not have solved the race: `Reserve` and `Release` are different keys, so a
  cancel and a submit racing each other would both commit. They collide on the document row instead.
- **Allotment changes are signed amounts, not new totals.** Two admins raising the same budget both get their
  raise, with no version to check and no 409 for the second. Lowering is a grant and is checked in the UPDATE;
  losing that race is a 409 `concurrency.conflict`.
- **Cost centre edits carry the version the editor saw** and a mismatch is a 409 `concurrency.conflict`; the
  row's `xmin` covers the moment between read and save. An edit that changes nothing keeps the version and
  publishes nothing.
- **Overspend is consumption beyond the allotment**, `max(0, -available)`. That includes every case of actual
  beyond the allotment, and also the earlier warning of actual plus open commitments beyond it.
- **The commitment rule is applied as written** even on an overspent budget where the commitment would free
  money (reservation larger than the order). Alternative: allow any entry that does not reduce available.
  Rejected to keep one rule and so the reconciliation "no reservation or commitment ever takes available below
  zero" holds entry by entry.
- **A commitment is not refused for an inactive cost centre.** The design refuses commitments only for money,
  and the requisition was reserved while the cost centre was active.
- **An invoice for an order Budgets never committed fails onto the error queue.** Payables only matches issued
  orders, and an order is issued only after Budgets committed it in the same transaction as its answer, so this
  cannot happen unless something upstream broke. Parking an actual with no budget would hide that.
- **A commitment on a different budget from its requisition's reservation fails the same way**
  (`funds.budget_mismatch`), rather than committing on one budget and releasing on another behind the
  requester's back.
- **Opening a budget needs an active cost centre.**
- **Timestamps are stored in UTC**: `OccurredAt` from other services is normalised on the way in.

## HTTP API

Behind the gateway at `/api/cost-centres` and `/api/budgets`; `http/budgets.http` walks through every endpoint.
Budget admins write. Budgets are read by admins, the auditor and the people who decide on spending against them
(approvers, finance approvers, the CFO). Cost centres are reference data any signed-in user may read, because a
requester picks one.

| Method | Path | Who | Answers |
|---|---|---|---|
| `POST` | `/cost-centres` | budget-admin | 201 cost centre at version 1; publishes `CostCentreChanged` |
| `GET` | `/cost-centres?after=&limit=` | signed in | a page in code order |
| `GET` | `/cost-centres/{code}` | signed in | the cost centre and its version |
| `PUT` | `/cost-centres/{code}` | budget-admin | 200; made against `version`, publishes the next one |
| `POST` | `/budgets` | budget-admin | 201 budget; the optional `id` becomes the budget's id |
| `GET` | `/budgets?fiscalYear=&after=&limit=` | readers | one year's balances in cost centre order |
| `GET` | `/budgets/{id}` | readers | the four figures, available and overspend |
| `POST` | `/budgets/{id}/allotment-changes` | budget-admin | 201 with the change and the balance right after it |
| `GET` | `/budgets/{id}/ledger?after=&limit=` | readers | the ledger in write order; `after` is the last sequence seen |
| `GET` | `/budgets/overspends?fiscalYear=&after=&limit=` | readers | one year's budgets consumed past their allotment |

Every create takes an optional client `id`. The first request's command and response are stored in
`idempotent_requests` in the same transaction as the create; a repeat with the same id gets the same status and
body, and the same id with different content is a 409 `request.id_reused`. Refusals are problem details with a
code: `cost_centre.already_exists`, `budget.already_exists`, `budget.allotment_below_consumed`,
`concurrency.conflict` and so on. A malformed body or query is a 400 with the failing fields. The OpenAPI
document at `/openapi/v1.json` names every response, including 401, 403 and each problem, and a test holds it
to that.

## Consumers and queues

Six thin consumers in Infrastructure, each calling its handler inside the transaction the outbox opened:

| Queue | Event | Answers with |
|---|---|---|
| `budgets-requisition-submitted` | `RequisitionSubmitted` | `FundsReserved` or `FundsReservationRejected` |
| `budgets-requisition-rejected` | `RequisitionRejected` | nothing |
| `budgets-requisition-cancelled` | `RequisitionCancelled` | nothing |
| `budgets-purchase-order-commitment-requested` | `PurchaseOrderCommitmentRequested` | `FundsCommitted` or `FundsCommitmentRejected` |
| `budgets-invoice-matched` | `InvoiceMatched` | nothing |
| `budgets-purchase-order-closed` | `PurchaseOrderClosed` | nothing |

`CostCentreChanged` leaves through the bus outbox from the cost centre endpoints. A consumer that loses a race
(an `xmin` conflict, or a unique index refusing a second row for a document) is retried by the shared policy and
finds the winner's row on the next attempt; both happen in the test run and show up as `R-RETRY` warnings. A
`BusinessRuleException` out of a consumer means a malformed message or a broken promise upstream (an invoice for
an order never committed) and goes straight to the `_error` queue.

## Metrics

Meter `Matchbook.Budgets`, exported with the rest over OTLP:

| Instrument | Tags | What it answers |
|---|---|---|
| `matchbook.budgets.reservations` (counter) | `outcome`, `reason` | how many requisitions got money, and why the rest did not |
| `matchbook.budgets.commitments` (counter) | `outcome`, `reason` | the same for purchase orders |
| `matchbook.budgets.overspends` (counter) | | invoices that took a budget from within its allotment to past it |
| `matchbook.budgets.grant.duration` (histogram, s) | `grant`, `outcome` | the conditional UPDATE, including the wait for the budget's row lock |

The histogram is the one to watch. Grants on one budget queue behind each other by design, so when a single
budget gets hot its p99 rises long before anything fails. A refusal and a budget going into overspend also
write one log line each, with the numbers.

## Tests

`tests/Services/Budgets/Matchbook.Budgets.UnitTests` (81 tests, under two seconds) covers the domain directly,
and runs FsCheck properties over random histories: up to six requisitions against one budget, each withdrawn or
ordered with up to three commitment attempts, up to three invoices over or under the order, and usually a close,
all shuffled into any order with random redeliveries. Three thousand histories per property check that the
figures equal the ledger, that replaying the ledger never finds a reservation or commitment below zero, that a
redelivery changes nothing, that the figures agree with the documents (reserved with what is held, committed per
order with its amount less its invoices, zero once closed, actual with the invoices), and, separately, that
invoices and closes end in the same place in any order. A sampling test checks the histories really reach
refusals, tombstones, retried attempts and overspends, and mutating the grant rule or the stale-attempt check
makes the properties fail within a few dozen cases. The properties drive the domain through a small in-memory
copy of each handler's sequence, with the guarded UPDATE replaced by `Budget.CanAfford`, so they say nothing
about the database.

`tests/Services/Budgets/Matchbook.Budgets.IntegrationTests` (32 tests, about a minute including the containers)
runs the real host against Postgres 18 and RabbitMQ 4.3 in containers, and talks to it only through HTTP and
real messages:

- **200 simultaneous reservations** of 1,000 against a budget of 50,000: exactly 50 `FundsReserved`, 150
  `FundsReservationRejected` with `insufficient_funds`, reserved 50,000, available 0, 50 `Reserve` entries, and
  the figures equal to the ledger.
- **50 simultaneous commitments** of 1,500, each taking over its own 500 reservation, against a budget with
  10,000 already spent and 25,000 left: exactly 25 committed and 25 refused, with the refused orders'
  reservations still standing and available at 0. The spent 10,000 is there on purpose. With actual at zero,
  the check constraint `reserved + committed <= allotted` is the same inequality as the grant, so a read-then-
  write bug would be caught by the constraint and hidden by the retries. With actual in the budget the
  constraint would allow 35, and only the conditional UPDATE holds it at 25. Replacing the UPDATE with
  read-then-write makes this test fail with 35 committed; the reservation test still passes under that bug,
  which is exactly why the second test exists.
- Redelivery under the same message id (the inbox drops it) and under a new id (the handler's own checks do):
  nothing changes and nothing new is answered, except a repeated commitment request, which is answered
  `FundsCommitted` again. A refused reservation sent again after funds were freed is still refused.
- Tombstones: a cancellation before its submission, and a `PurchaseOrderClosed(Cancelled)` before its
  commitment request, which releases the requisition's reservation exactly once (also on redelivery) and answers
  the request `document_closed`. A stale attempt gets no answer and commits nothing; only a newer one is decided.
- Invoices then close, and close then invoices, on two identical budgets: identical figures.
- Over HTTP: `CostCentreChanged` at versions 1, 2, 3; a stale version is a 409; allotment rules; idempotent
  creates for all three create endpoints; 401 and 403 by role; ledger and list paging; the overspend report;
  the OpenAPI document.

Measured on a laptop (Intel Core Ultra 7 255H, 32 GB, Docker Desktop on WSL2, both containers local), from the
first publish to the last answer arriving at the probe, over five runs: the 200 reservations took 1.6 to 4.9
seconds, the 50 commitments 0.4 to 1.2 seconds. All grants on one budget queue on its row lock, and each holds
it until its transaction commits (outbox rows and inbox record included), so that is the budget's ceiling:
a few hundred decisions a second on one budget, and many budgets in parallel. These are test timings, not a
benchmark, and are not a throughput claim.

## Decisions made while wiring the service

- **Idempotent creates keep the first response, not only the first request.** A repeat has to get the same
  body, and a cost centre may have been renamed between the first request and its retry; its version 1 cannot
  be rebuilt from the current row. Rejected: deriving the response from the created resource (wrong after any
  change) and a hash of the request alone (proves a repeat, cannot answer it). The stored command is compared as
  a record, so `100` and `100.00` are the same amount.
- **The client's id becomes the resource's id where there is one**: the budget's id, and the allotment change's
  document id in the ledger. The ledger's unique key then refuses a double-applied change even if the
  idempotency row were somehow lost.
- **A replay publishes nothing.** The first request's event left with its transaction.
- **Two identical requests racing** both miss the stored response; the second fails on a unique index and gets a
  409 `request.in_progress` (or the resource's own duplicate code), and its retry gets the stored answer.
  Rejected: an advisory lock per id, which would make every create pay for a race a client causes by sending
  twice at once.
- **Idempotency rows are kept.** Creates are rare admin actions, so the table grows by a few rows a day. A
  retention job is the upgrade if that changes.
- **An allotment change is `POST /budgets/{id}/allotment-changes` with a signed amount**, answered 201 with the
  change and the balance right after it, read inside the transaction under the row lock. No `Location`: a change
  has no address of its own, and it appears in the ledger under its id. Rejected: `PUT /budgets/{id}/allotment`
  with a new total, which loses one of two concurrent edits or needs a token every reservation invalidates.
- **Responses are the application layer's view records** (`BudgetView`, `CostCentreView`, `LedgerEntryView`,
  `Page<T>`), which are already shaped for the API and are not domain types; requests are separate API records
  that carry the shape validation. A second set of identical response records would be one more mapping to keep
  in step for no difference today.
- **Shape at the edge, rules in the domain.** A missing field, a year outside 2000 to 2100 or a `limit` above 200
  is a 400 from validation; a code off the pattern, a zero allotment change or an inactive cost centre is a 409
  or 422 from the domain, with a code.
- **Read access**: approvers, finance approvers and the CFO read budgets because they approve spending against
  them; requesters do not. Cost centres are readable by anyone signed in.
- **The meter is static**, not from `IMeterFactory`: the factory's package is outside what the architecture test
  allows the application layer, and OpenTelemetry finds a meter by name either way.
- **The overspend counter counts transitions**, an invoice that takes a budget from within its allotment to past
  it, not every invoice on a budget already over. That is the event someone has to act on.

## What the shared plumbing provides

The consumer outbox runs at READ COMMITTED, which is what the grant needs (the UPDATE waits for the row lock and
then re-evaluates its WHERE clause on the new row; under REPEATABLE READ, MassTransit's own default, it would
fail with a serialization error instead). `AddMatchbookDatabase` applies the snake_case names the migrations
use and runs no retrying execution strategy, so the handlers' own transaction on the API path is safe. Retries
cover everything but a `BusinessRuleException`.
