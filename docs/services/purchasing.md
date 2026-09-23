# Purchasing

Purchasing turns an approved requisition into a purchase order, gets the money committed by Budgets, issues
the order, records what arrives, and closes the order once the invoices for it are matched. It owns:

- **purchase orders** and their lines, each line carrying running totals of what was received and invoiced;
- **goods receipts**, one row per delivery, the record of how those totals got where they are;
- **matched invoices**, one row per invoice already counted, which is what makes `InvoiceMatched` idempotent;
- a **local copy of each supplier's standing** (active or not, and the snapshot version), from `SupplierChanged`.

Every rule is decided in `PurchaseOrder` against the running totals. Receipts and matched invoices are separate
tables rather than collections loaded with the order, so loading an order for a change costs the same on its
first day as after its fiftieth delivery.

## States

```
RequisitionApproved
        │
        ▼
      Draft ──issue──▶ CommitmentPending ──FundsCommitted (attempt n)──▶ Issued ──last invoice settles──▶ Completed
        ▲                     │                                           │
        └──FundsCommitmentRejected (attempt n)                            └──short-close──▶ ShortClosed

Cancelled: from Draft, from CommitmentPending, or from Issued while nothing has been received.
```

`Completed`, `ShortClosed` and `Cancelled` are ends. Each publishes `PurchaseOrderClosed` with that reason, and
Budgets releases whatever it still holds for the order.

## Rules

Every code starts with `purchase_order.`; the table drops the prefix after the first one.

| Rule | Code | Why |
|---|---|---|
| Only a `buyer` amends, issues, short-closes or cancels; only a `receiver` records receipts | `purchase_order.not_a_buyer`, `.not_a_receiver` (403) | Checked by the API's policies too. Checking in the domain as well means an endpoint wired to the wrong policy fails closed. |
| The buyer who issued an order may not receive against it, whatever roles they hold | `purchase_order.receiver_is_buyer` (403) | Separation of duties: the person who committed the money is not the person who confirms the goods arrived. |
| Only a draft is amended; quantity and unit price never below zero and never finer than their columns | `.not_draft` (409), `.negative_quantity`, `.quantity_precision`, `.negative_unit_price`, `.unit_price_precision`, `.amount_too_large` (422) | Postgres would round surplus decimals silently and refuse surplus digits as a 500. The amount check divides before multiplying, because at the top of both ranges the product overflows `decimal` itself. |
| Issue needs the supplier active in the local copy, and at least one line above zero | `.supplier_not_active`, `.nothing_ordered` (409) | A supplier Purchasing has never heard of, or one with a status it does not recognise, counts as not active. |
| A receipt takes a line to at most what was ordered; quantities above zero, three places | `.over_receipt` (409), `.receipt_quantity_not_positive`, `.empty_receipt`, `.unknown_line` (422) | All lines of a receipt are checked before any is applied, so a refused receipt changes nothing. |
| Invoiced never exceeds received | `.invoiced_exceeds_received` (409) | See "When Payables and Purchasing disagree" below. |
| An order with anything received cannot be cancelled, only short-closed | `.goods_received`, `.closed` (409) | Goods on the shelf are a fact the order has to keep. |

Line amounts always come from `Amounts.Line(quantity, unitPrice)` and the order amount is their sum. The amount
on an incoming requisition line is ignored and recomputed, so the one rounding rule is the only one that ever
runs here.

## Staying correct under reordering and redelivery

**`RequisitionApproved`** drafts one order per requisition. The handler looks for an order with that
requisition id first; two copies consumed at the same moment both miss, and the unique index on
`requisition_id` fails the second insert, which is retried and then finds the first order.

**`SupplierChanged`** is applied with one conditional `UPDATE ... WHERE id = @id AND version < @version`. The
database decides the version check under the row lock, so two snapshots consumed at once cannot leave the older
one in place, which a read-then-write would allow. If nothing was updated and the row does not exist, it is
inserted; two first sights race on the primary key, the loser retries, and takes the update path.

**`FundsCommitted` and `FundsCommitmentRejected`** act only when the order is `CommitmentPending` and the reply
carries the attempt now in flight. Anything else is ignored and logged: a reply to an older attempt, a reply to
an attempt never sent, a redelivered reply (the order is no longer pending), or a reply to an order the buyer
cancelled. A property test shuffles every genuine reply, delivered one to three times, together with stray
replies, and checks that exactly one reply takes effect and that its time and reason survive every later copy.

**`InvoiceMatched`** is counted once per invoice id: the handler looks for the invoice in `matched_invoices`
first, and the primary key on `invoice_id` catches the concurrent duplicate. The order always exists by the
time an invoice arrives, because Payables only matches against orders it heard of from `PurchaseOrderIssued`,
which Purchasing publishes after saving the order. A missing order is therefore a defect, not something to wait
for, and the message fails.

### When Payables and Purchasing disagree

Payables never matches more than was received, and it only learns of a receipt from `GoodsReceived`, which
Purchasing publishes after the receipt is saved. So an `InvoiceMatched` that would take a line's invoiced
quantity past its received quantity cannot be a race; it is a bug on one side. Purchasing refuses it: the
consumer throws, the message is retried and then parked on the `_error` queue, and the order keeps totals that
are true. Clamping to what was received was rejected because it hides the disagreement and leaves the two
services with different numbers that nobody is told about. Accepting it was rejected because it breaks the
invariant the system tests check across services, and the database would refuse it anyway.

The retries are wasted on a message that will never succeed. They are bounded and rare; if they matter, the
retry policy in BuildingBlocks can skip `BusinessRuleException`.

### Cancelling while a commitment request is in flight

The buyer may cancel an order in `CommitmentPending`. The order becomes `Cancelled` at once and
`PurchaseOrderClosed(Cancelled)` goes to Budgets, while `PurchaseOrderCommitmentRequested` may still be on its
way. Budgets consumes the two from different queues, in either order:

- **Request first.** Budgets commits the order's amount (releasing the requisition's reservation), then the close
  releases that commitment. Budgets holds nothing. Its `FundsCommitted` reaches Purchasing, finds a cancelled
  order, and is ignored: no `PurchaseOrderIssued` is published.
- **Close first.** The order was never committed, so the close releases the requisition's reservation, and
  Budgets records that the order is closed. The request arrives later and is refused with `document_closed`.
  Budgets holds nothing. The `FundsCommitmentRejected` is ignored by Purchasing.

The second case depends on Budgets leaving a tombstone when it closes an order it never committed, as the
design asks of every cancellation that can overtake the thing it cancels. Purchasing cannot make up for a
missing tombstone on its own: sending `PurchaseOrderClosed` again on a late `FundsCommitted` would be dropped
by Budgets' own idempotency on (document, step). A property test interleaves a cancel at every position among
redelivered replies and checks that the order always ends cancelled and that no reply after the cancel takes
effect.

If `FundsCommitted` is applied while the cancel is being saved, one of the two loses on the concurrency token.
If the reply loses, it is retried, finds the order cancelled, and is ignored. If the cancel loses, the buyer
gets a 409 and reloads an order that is now issued with nothing received, which they can still cancel; that
close releases the commitment.

## Concurrency

The order row carries Postgres' `xmin` as its concurrency token. Postgres checks it only on rows an `UPDATE`
touches, and recording a receipt or an invoice changes only lines, so the DbContext marks the order row modified
whenever anything inside the order changed. Without that, two concurrent receipts would both pass the
over-receipt rule against the same totals and the second would overwrite the first. This was checked by hand
against Postgres 18 while building it: with the marking removed the second receipt saved silently, with it the
second save raised a concurrency exception. An integration test will hold that once the shared harness lands.

## Database constraints

The rules the rest of the system relies on are enforced by Postgres too, so a bug in a handler, or a manual fix
in production, cannot break them quietly:

| Table | Constraint |
|---|---|
| `purchase_orders` | unique `requisition_id` (one order per requisition); unique `number`; `status` in the six known values; `amount >= 0`; `commitment_attempt >= 0` |
| `purchase_order_lines` | primary key (`purchase_order_id`, `line_number`); `line_number > 0`; `quantity >= 0`; `unit_price >= 0`; `0 <= received_quantity <= quantity`; `0 <= invoiced_quantity <= received_quantity` |
| `goods_receipt_lines` | primary key (`goods_receipt_id`, `line_number`); `quantity > 0` |
| `matched_invoices` | primary key `invoice_id` (an invoice is counted once) |
| `goods_receipts`, `matched_invoices` | foreign key to the order, `ON DELETE RESTRICT` |

Money is `numeric(18,2)`, quantities `numeric(18,3)`, unit prices `numeric(18,4)`. The list endpoint's filter
and keyset are served by an index on (`status`, `id`).

## Decisions the design left open

**A line set to zero stays on the order.** Removing it was the alternative. Keeping it means a buyer can bring
the line back before issuing, line numbers stay the requisition's, and the order shows that someone decided not
to buy that line rather than the line vanishing. A zero line is settled from the start, so it never holds up
completion. Issuing needs at least one line above zero.

**One number sequence, not one per year.** Numbers are `PO-<UTC year drafted>-<sequence>`, the sequence padded
to six digits and allowed to grow past them. A sequence per year would need creating a sequence at runtime on
the first order of each year (DDL from a message consumer, with its own race on the catalog) or creating years
ahead in migrations. A counter table would give gapless numbers but serialise every draft on one row lock held
until the consumer's transaction commits. What was given up: the counter does not restart at 1 in January, and
a rolled-back draft leaves a gap. Neither matters for a purchase order number, which has to be unique and
readable, not dense. The year is the year the order was drafted, not the requisition's fiscal year, because a
document number records when the document was made.

**Any buyer, not only the issuing one, may amend, issue, short-close or cancel.** Orders are worked by a
purchasing team; tying them to one person strands them when that person is away. The order records who issued
it and who closed it.

**Any issued order can be short-closed.** Requiring goods still outstanding was the first version. It blocked
the case that most needs a short-close: received in full, invoiced in part, and the rest never billed. Budgets
would have held that commitment forever. An issued order is never fully settled anyway, because the invoice
that settles it completes it.

**Repeated line numbers in one receipt or invoice are added together** rather than refused. The receipt stores
one line per order line, and `GoodsReceived` carries the sums. An invoice is a fact from Payables, and refusing
a legitimate one because it named a line twice would park it for nothing.

**Receipt ids are generated by the server.** A client that retries a receipt after a timeout could record it
twice; the over-receipt rule bounds the damage but does not prevent it. A client-supplied receipt id (or an
`Idempotency-Key` header) would close that gap and belongs with the API in the next phase.

**Rejection clears the issuing buyer.** The next buyer to issue the order is the one the receipt rule compares
against. Cancelling a pending order keeps it, as a record of who sent it for commitment.

**The supplier copy holds only the standing and the version.** Name, country and bank details belong to the
Suppliers service and nothing here reads them.

**Lists are newest first, keyed on the id.** Version 7 ids sort by creation time, so the cursor is the last id
seen and the next page is one index range scan however deep the reader goes. The limit is clamped to 1 to 200.

## For the next phase

- The host must register the DbContext with `UseSnakeCaseNamingConvention()`, as the design-time factory does,
  or the model will not match the migration.
- `DbUpdateConcurrencyException` from a handler is the 409 `concurrency.conflict` the design asks for.
- The MassTransit consumers are thin: each calls the handler of the same name in
  `Matchbook.Purchasing.Application.IncomingEvents`.
