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
consumer throws a `BusinessRuleException`, which the shared retry policy does not retry, so the message goes
straight to the `_error` queue, and the order keeps totals that are true. Clamping to what was received was
rejected because it hides the disagreement and leaves the two services with different numbers that nobody is
told about. Accepting it was rejected because it breaks the invariant the system tests check across services,
and the database would refuse it anyway.

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
effect. Over real RabbitMQ, `IssuingTests` cancels an order while the request is out, then delivers
`FundsCommitted` for it: the order stays `Cancelled`, `PurchaseOrderClosed` is published exactly once, and
`PurchaseOrderIssued` never is. Budgets proves its half, the tombstone, in its own tests.

If `FundsCommitted` is applied while the cancel is being saved, one of the two loses on the concurrency token.
If the reply loses, it is retried, finds the order cancelled, and is ignored. If the cancel loses, the buyer
gets a 409 and reloads an order that is now issued with nothing received, which they can still cancel; that
close releases the commitment.

## Concurrency

The order row carries Postgres' `xmin` as its concurrency token. Postgres checks it only on rows an `UPDATE`
touches, and recording a receipt or an invoice changes only lines, so the DbContext marks the order row modified
whenever anything inside the order changed. Without that, two concurrent receipts would both pass the
over-receipt rule against the same totals and the second would overwrite the first. With the marking removed,
a hand-run check against Postgres 18 saved the second receipt silently.

`ReceivingTests` holds it: twelve times, two receipts of 6 against a line ordered at 10 are sent at the same
moment. Each round ends with exactly one 201 and one 409, the line at 6, and one `GoodsReceived`. On the
development machine (Windows 11, Docker Desktop on WSL 2, Postgres 18.6 in a container) all twelve refusals were
`concurrency.conflict`: both requests had read the order before either saved, so it was the row version, not
the over-receipt rule, that stopped the second.

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

**Repeated line numbers in one receipt or invoice are added together** rather than refused. The contracts now
say each order line appears at most once per receipt and per invoice; summing stays as a defence. The receipt
stores one line per order line, and `GoodsReceived` carries the sums. An invoice is a fact from Payables, and
refusing one because it named a line twice would park it for nothing.

**Rejection clears the issuing buyer.** The next buyer to issue the order is the one the receipt rule compares
against. Cancelling a pending order keeps it, as a record of who sent it for commitment.

**The supplier copy holds only the standing and the version.** Name, country and bank details belong to the
Suppliers service and nothing here reads them.

**Lists are newest first, keyed on the id.** Version 7 ids sort by creation time, so the cursor is the last id
seen and the next page is one index range scan however deep the reader goes. The limit is clamped to 1 to 200.

## API

Everything is under `/purchase-orders` (`/api/purchase-orders` through the gateway), needs a token, and is
described in `/openapi/v1.json` with its request, response and problem types. `http/purchasing.http` walks
through it in order.

| Method and path | Role | Success | Refusals worth knowing |
|---|---|---|---|
| `GET /purchase-orders?status=&after=&limit=` | buyer, receiver, auditor | 200 page, newest first, `next` cursor | 400 for an unknown status |
| `GET /purchase-orders/{id}` | buyer, receiver, auditor | 200 order with its lines | 404 `purchase_order.not_found` |
| `GET /purchase-orders/by-requisition/{requisitionId}` | buyer, receiver, auditor | 200 order | 404 until the approval is processed |
| `PUT /purchase-orders/{id}/lines/{lineNumber}` | buyer | 200 order | 400 missing value, 409 `not_draft`, 422 below zero or too precise |
| `POST /purchase-orders/{id}/issue` | buyer | 202 order in `CommitmentPending` | 409 `supplier_not_active`, `nothing_ordered`, `not_draft` |
| `POST /purchase-orders/{id}/short-close` | buyer | 200 order | 409 `not_issued` |
| `POST /purchase-orders/{id}/cancel` | buyer | 200 order | 409 `goods_received`, `closed` |
| `POST /purchase-orders/{id}/receipts` | receiver | 201 receipt, `Location` | 403 `receiver_is_buyer`, 409 `over_receipt`, `request.id_reused` |
| `GET /purchase-orders/{id}/receipts` | buyer, receiver, auditor | 200 receipts, oldest first | 404 |
| `GET /purchase-orders/{id}/receipts/{receiptId}` | buyer, receiver, auditor | 200 receipt | 404 `goods_receipt.not_found` |

A lost race on the order row is 409 `concurrency.conflict` wherever it happens; read the order and try again.

## Messaging

Each consumer is a thin MassTransit adapter over the Application handler of the same name in
`IntegrationEvents/`, which it asks for as `IIntegrationEventHandler<TEvent>`, behind the shared outbox and
inbox. Queues are named for the service and the event.

| Event | Queue | Idempotent through |
|---|---|---|
| `RequisitionApproved` | `purchasing-requisition-approved` | unique `requisition_id` |
| `FundsCommitted` | `purchasing-funds-committed` | attempt number and state |
| `FundsCommitmentRejected` | `purchasing-funds-commitment-rejected` | attempt number and state |
| `SupplierChanged` | `purchasing-supplier-changed` | version in the `WHERE` of the update |
| `InvoiceMatched` | `purchasing-invoice-matched` | primary key on `invoice_id` |

Purchasing publishes `PurchaseOrderCommitmentRequested`, `PurchaseOrderIssued`, `GoodsReceived` and
`PurchaseOrderClosed`, always through the outbox in the same transaction as the change.

## Metrics

On the `Matchbook.Purchasing` meter, recorded after the change they count was saved:

| Instrument | Kind | Tags | Read it as |
|---|---|---|---|
| `matchbook.purchasing.orders.drafted` | counter | | approvals turned into drafts |
| `matchbook.purchasing.orders.issued` | counter | | orders Budgets committed |
| `matchbook.purchasing.commitments.rejected` | counter | `reason` | refusals from Budgets; a rise in `insufficient_funds` is a budget problem, not a Purchasing one |
| `matchbook.purchasing.receipts.recorded` | counter | | deliveries recorded |
| `matchbook.purchasing.orders.closed` | counter | `reason` (`Completed`, `ShortClosed`, `Cancelled`) | how orders end; many short-closes point at suppliers who under-deliver |
| `matchbook.purchasing.orders.time_to_issue` | histogram, seconds | | from draft to issue: how long approved requisitions wait for a buyer |

`MetricsTests` drives one order through a refusal, an issue, a receipt and a short-close and checks each
instrument saw it.

## Decisions made when wiring the service

**Creates are idempotent on the receipt id.** The client sends its own `id`; the same request again returns the
first receipt, byte for byte, and the same id for a different receipt is 409 `request.id_reused`. "The same
request" means the same order, the same person and the same quantity per line. Ignoring the person was
rejected: a second person posting someone else's id is not retrying, and answering them with a receipt they
did not record would hide the mistake. The check runs before the business rules, so a retry still gets its
receipt after the order has been short-closed.

**A new receipt is answered from the stored row.** The first version returned the object in memory, and the
integration test comparing a first answer with its retry caught the difference: the request's `2` against the
column's `2.000`, and a time to 100 ns against Postgres' microsecond. Normalising scale and time in the domain
was the alternative; reading the row back costs one indexed query on a rare request and makes the two answers
equal by construction rather than by care.

**Two identical receipts racing each other.** Both miss the replay check; the loser then fails on the order's
row version or on the receipt's primary key. Both are answered as `concurrency.conflict`, and the client's retry
finds the winner's receipt. Catching the failure and answering from the winner inside the same request was
rejected as a second code path for a case the client's own retry already handles.

**Receipts are a sub-resource and the answer is the receipt, not the order.** The order changes after the
receipt, so an answer carrying it could not be repeated byte for byte. `Location` is relative
(`receipts/{id}`) so it resolves the same with or without the gateway's `/api` prefix, which an absolute path
would drop.

**Issue answers 202.** The order is only waiting for Budgets when the request returns; 200 would read as
issued. The body is the order in `CommitmentPending`.

**The order for a requisition has its own route.** A `requisitionId` filter on the list was the alternative; it
would answer "not drafted yet" with an empty page and a 200, where a 404 with a code says so plainly.

**Request bodies have nullable, required values.** A non-nullable `decimal` reads a missing `quantity` as
zero, and zero is a valid quantity: an amendment without one would quietly empty the line. Missing values are a
400 naming the field; values that are present but wrong are the domain's 422 or 409 with a rule code.

**The Application views are the response bodies.** They exist only to be returned by this API, so Api copies
of each would be field-for-field mappings with nothing to protect.

**Receivers can read orders.** They have to find what to receive. Anyone without a purchasing role, a requester
for instance, gets a 403 even on reads.

**The meter is created in Application with `new Meter`.** `IMeterFactory` lives in a package the architecture
tests keep out of the inner layers, and the handlers are where the outcomes are known. Time to issue is
measured from the draft, which is made when the approval is consumed. Storing the approval's own time was
rejected: a column and a migration to remove what is normally milliseconds of consumer lag.

**No new migration.** Nothing in phase 2 changed the model; the client-chosen receipt id uses the existing key.
