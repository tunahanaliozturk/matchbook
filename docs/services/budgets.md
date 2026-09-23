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
reservations against room for fifty grant fifty. The domain states the same inequality as
`Budget.CanAfford`.

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

Available itself has no constraint: an invoice larger than its commitment is allowed to take it below zero,
and the floor grants respect is enforced by their UPDATE.

## Decisions the design left open

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

## Tests

`tests/Services/Budgets/Matchbook.Budgets.UnitTests` covers the domain directly, and runs FsCheck properties
over random histories: up to six requisitions against one budget, each withdrawn or ordered with up to three
commitment attempts, up to three invoices over or under the order, and usually a close, all shuffled into any
order with random redeliveries. Three thousand histories per property check that the figures equal the
ledger, that replaying the ledger never finds a reservation or commitment below zero, that a redelivery changes
nothing, that the figures agree with the documents (reserved with what is held, committed per order with its
amount less its invoices, zero once closed, actual with the invoices), and, separately, that invoices and
closes end in the same place in any order. A sampling test checks the histories really reach refusals,
tombstones, retried attempts and overspends, and mutating the grant rule or the stale-attempt check makes the
properties fail within a few dozen cases.

The properties drive the domain through a small in-memory copy of each handler's sequence, with the guarded
UPDATE replaced by `Budget.CanAfford`. They say nothing about the database. The concurrency claim, the
constraints and the outbox are for the integration tests against real Postgres.

## For the messaging setup

- The grant relies on READ COMMITTED re-checking the WHERE clause after waiting for the lock. MassTransit's EF
  outbox opens the consumer transaction at REPEATABLE READ unless told otherwise; there a concurrent grant fails
  with a serialization error (40001) instead, which a retry absorbs but a burst turns into a retry storm.
  Budgets' consumer endpoints should run at READ COMMITTED.
- A consumer that loses a race gets `DbUpdateConcurrencyException` or a unique violation. Both should be
  retried, not faulted.
- The runtime DbContext registration needs `UseSnakeCaseNamingConvention()` to match the migration.
- The handlers open their own transaction when none is running (API requests) and join the outbox's otherwise.
  If the DbContext is registered with a retrying execution strategy, that path needs wrapping in it.
