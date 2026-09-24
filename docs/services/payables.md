# Payables

Payables owns supplier invoices, the three-way match against purchase orders and goods receipts, and payment
runs. It keeps local copies of what it needs from other services (orders, receipts, suppliers), publishes
`InvoiceMatched` and `InvoicePaid`, and produces the ISO 20022 `pain.001.001.09` file a bank executes.

The rules come from the Payables section of [the design](../design.md). This note covers how they are held, and
the decisions the design left open.

## Invoice states

| State | Meaning | Leaves when |
|---|---|---|
| `SuspectedDuplicate` | Same supplier and total as another invoice, dated within seven days, under a different number | An AP approver who did not capture it clears it; it is then matched |
| `AwaitingPurchaseOrder` | The order has not arrived | `PurchaseOrderIssued` or a cancellation arrives |
| `AwaitingReceipt` | A line would bill more than was received | Goods arrive |
| `PriceVariance` | A price is outside tolerance | An AP approver who did not capture it accepts it with a reason |
| `Rejected` | It can never match: order cancelled, another supplier's order, or a line the order does not have | Never. The number can be captured again |
| `Payable` | Matched; `InvoiceMatched` published | A payment run takes it |
| `Scheduled` | In a draft payment run | The run is released (paid) or cancelled, or its supplier is dropped at release (payable again) |
| `Paid` | Paid by a released run; `InvoicePaid` published | Never |

`Captured` exists only inside the capture request, between building the invoice and deciding what happens to it.

## The three-way match

`ThreeWayMatch.Evaluate(invoice, order, position)` is a pure function. The position holds, per order line, what
has been received and what matched invoices have already billed. The first check that fails decides:

1. The order was closed as `Cancelled`: rejected. This comes first because a cancellation can arrive for an order
   Payables never saw issued (Purchasing closes drafts too).
2. The order has not arrived: `AwaitingPurchaseOrder`.
3. The order is with another supplier, or lacks a billed line number: rejected.
4. On some line, everything invoiced so far plus this invoice exceeds what was received: `AwaitingReceipt`.
5. On some line, `|invoiced - ordered| x quantity` exceeds `min(2% of Amounts.Line(quantity, ordered), 100.00)`,
   and no approver accepted it: `PriceVariance`.
6. Otherwise matched, due on the invoice date plus the supplier's payment terms.

Every result carries an outcome, a reason code and a sentence for people, and once the order is known, the numbers
for every line. The invoice keeps the reason and the sentence, so the API can say why an invoice is waiting.

**Why these tolerances.** The percentage keeps small lines honest; the cap stops a large line from hiding real
money inside a share that looks like rounding (2% of a 50,000.00 line is 1,000.00). The comparison is exact: the
variance is not rounded, so a variance equal to the allowance passes and one ten-thousandth of a euro more does
not. FsCheck properties pin that edge in both regimes, with the boundary worked out from the rule as written
rather than from the code.

**Several invoices on one order.** Invoices waiting on an order are matched oldest capture first, and each one that
matches is added to the position before the next is evaluated, so between them they never bill more than was
received. A property interleaves random receipts and invoices and checks, after every step, that no line is
invoiced beyond what was received and that nothing is left waiting that would fit.

## Duplicates

Invoice numbers are normalised: upper case, letters and digits only, leading zeros dropped from each run of digits
(`INV-0042` and `inv42` are one number). A run of digits is what remains between letters once punctuation is gone,
and a run of zeros keeps one zero, so `INV-0` is not `INV`. The number is unique per supplier among invoices that
are not rejected, checked by the handler and enforced by a partial unique index; a second capture is a 409
`invoice.duplicate`.

The suspected-duplicate rule (same supplier and total, dates at most seven days apart, different number) is a
domain function over the few invoices with the same supplier and total, which the handler fetches by index.

## Paying at most once

Three things hold it, and any one of them failing still leaves the others:

- **One active run per invoice, in the database.** `payment_run_items` has a unique index on `invoice_id` where the
  item is `Scheduled` or `Paid`. Two drafts taken at the same moment read the same candidates; the second insert
  waits for the first to commit and then fails. The context turns that violation into a 409
  `payment_run.invoice_taken`, and the transaction takes the whole losing draft with it.
- **Release happens once.** Release starts by writing the run under its row version (`xmin`). A second release,
  concurrent or later, either fails on that version or finds the run already released, before anything is paid.
  Cancel works the same way, so a release and a cancel cannot both win.
- **Every set-based statement is guarded and counted.** Items and invoices move with one statement each, each guarded
  by the status it expects (an invoice is only paid while it is `Scheduled`). The release compares the rows it paid
  with the count the domain decided on and rolls everything back if they differ.

At release, each supplier in the run is checked against its current local copy: blocked, unknown, or on a different
account version means its invoices are dropped and become payable again. The bank details written into the file were
recorded at the draft, and an unchanged account version means they are still the ones on record.

## Staying correct under reordering and redelivery

Any event can arrive before the one it depends on, and any event can arrive twice.

- **Orders.** The local order row is created the first time anything mentions the order: an invoice, a receipt, a
  closure or the order itself. `PurchaseOrderIssued` fills it in once; a redelivery changes nothing. A closure is
  kept even when the order has not arrived, so a later invoice for a cancelled order is still refused.
- **Receipts** are stored by receipt id whether or not the order is known. A receipt already stored is a redelivery.
- **Suppliers** are applied only when the snapshot's version is newer than the one held. A status Payables does not
  recognise is treated as not active.
- **Matching serialises on the order row.** Every change that can alter a match on an order (the order arriving, a
  closure, a receipt, an invoice captured or released by an approver) writes the order's row in the same save,
  bumping its `revision`, which is the row's concurrency token. Two of them that started from the same revision
  cannot both commit, and two that both create the row collide on its key; either way the loser is retried as a
  consumer or answered with a 409. Without this, a receipt and an invoice arriving together could each miss the
  other, or two invoices could each take the last received quantity.
- When a missing piece arrives, every invoice waiting on that order is matched again.

## Database constraints

| Constraint | Holds |
|---|---|
| `ux_invoices_supplier_number` on `(supplier_id, normalised_number) WHERE status <> 'Rejected'` | One live invoice per supplier number |
| `ux_payment_run_items_active_invoice` on `invoice_id WHERE status IN ('Scheduled', 'Paid')` | An invoice in at most one active run |
| `pk_receipts` on the receipt id | A receipt counted once |
| `pk_purchase_orders`, `revision` as concurrency token | Matches on one order serialise |
| `xmin` row versions on invoices, payment runs and suppliers | No silent overwrite |
| `ck_invoices_variance_second_person`, `ck_invoices_duplicate_second_person` | The approver is not the capturer |
| `ck_payment_runs_released_by_second_treasurer` | The releaser is not the drafter |
| Positive totals, amounts and quantities; prices not negative | Arithmetic the rules assume |

There are no foreign keys between local copies (orders, receipts, suppliers) or from invoices to them, because they
arrive in any order. Payment run items do reference their run and their invoice, both of which Payables owns.

## Decisions the design left open

- **Quantity is checked before price.** Rejected alternative: price first. A shortfall clears itself when goods
  arrive; an approver should only see a variance when it is the one thing between the invoice and payment.
- **Digit runs are delimited by letters, not punctuation.** Rejected alternative: runs as written, so `2026-0042`
  equals `2026-42`. That version is not idempotent (`0-5` becomes `05`, which becomes `5`), and normalising a stored
  value again must not change it. The cost is that `2026-0042` and `2026-42` are different numbers; the
  suspected-duplicate rule is the second net for that case.
- **A rejected invoice does not hold its number.** Rejected alternative: unique among all invoices. A clerk who
  typed the wrong order must be able to capture the invoice again.
- **Clearing a suspected duplicate needs an approver who did not capture it**, the same four-eyes rule as accepting a
  variance. The design only said "held for an ap-approver".
- **A line the order does not have rejects the invoice.** Order lines are final once issued, so waiting cannot help.
- **Only a `Cancelled` closure changes a match.** A short-closed or completed order still matches what was received,
  and an invoice waiting for goods on it keeps waiting, since a receipt may still be in flight. An unknown close
  reason is treated as not cancelling: rejecting on a value Payables cannot read is the less cautious choice.
- **An invoice matched before its supplier arrives is payable without a due date.** Rejected alternative: a separate
  `AwaitingSupplier` state. `InvoiceMatched` matters to Budgets and Purchasing whatever the replication lag, and no
  run pays a supplier Payables does not know. A run treats the missing due date as the invoice date plus the
  supplier's terms at the time of the run.
- **Serialise matches on the order row.** Rejected alternatives: `SERIALIZABLE` transactions, which depend on how
  each host opens its transaction and fail on unrelated predicate conflicts; and running totals on the order row,
  which a receipt arriving before its order cannot update.
- **Decide per supplier at release, and record bank details per supplier at the draft.** Rejected alternative: a
  decision and a bank snapshot per invoice. The rule is about the supplier, a run has far fewer suppliers than
  invoices, and the invoice-level changes become a handful of set-based statements: releasing 100,000 invoices is
  a few updates, not 100,000.
- **A lost draft race is a 409, not a retry.** Rejected alternative: retry with whatever is left. That silently makes
  a second run for the same date from the leftovers, which a treasurer should decide on.
- **Dates are UTC calendar days.** A run cannot be drafted for a past date, and cannot be released once its
  execution date has passed (banks reject a past requested execution date); cancel and draft again.
- **A run needs at least one payment.** Drafting with nothing due is a 409 `payment_run.nothing_due`, and releasing a
  run in which every supplier would be dropped is a 409 `payment_run.nothing_payable` that leaves the draft as it
  was: pain.001 has no empty file.
- **Any treasurer may cancel a draft, the drafter included.** Cancelling moves no money, so it needs no second person.
- **The file.** `MsgId` and `PmtInfId` are the run id, `EndToEndId` the invoice id (both 32 hex characters),
  `CreDtTm` the release time in UTC, `Cdtr/Nm` the verified account holder, and names are cut to the 140 characters
  the schema allows. The debtor comes from configuration when the file is downloaded. It is written with
  `XmlWriter` as rows are read, and the writer throws rather than finish a file whose transactions do not add up to
  its header.
- **A receipt that lists a line twice is summed** rather than refused, since a consumer that throws would park the
  receipt for good.
- **Lists are keyset-paginated by id** (version 7 GUIDs are time ordered): invoices newest first, the exceptions queue
  (suspected duplicates and price variances only) oldest first.
- **Enums are stored by name**, so index filters and ad hoc queries read as English.
- **IBAN and BIC are value objects**, mapped through one converter in `BankColumns`, which is where encryption at rest
  plugs in. `Iban.ToString()` prints only the last four characters.
