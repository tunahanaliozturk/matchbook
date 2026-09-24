# Requisitions

Requisitions owns the request to buy something and the approvals it needs before Purchasing may order it. It
keeps each requisition's lines, its approval route, and a timeline of who did what. It also keeps local copies
of cost centres and suppliers, taken from `CostCentreChanged` and `SupplierChanged`, so that submitting and
routing never wait on another service.

## States

```
Draft ──submit──▶ Submitted ──FundsReserved──▶ PendingApproval ──last step──▶ Approved ──PurchaseOrderIssued──▶ Ordered
                      │                              │                            │                                │
          FundsReservationRejected           a step rejected            PurchaseOrderClosed             PurchaseOrderClosed
                      ▼                              ▼                            ▼                                │
               BudgetRejected                    Rejected                      Closed ◀─────────────────────────────┘

Draft, Submitted or PendingApproval ──cancel──▶ Cancelled
```

`BudgetRejected`, `Rejected`, `Cancelled` and `Closed` are ends. Only the requester edits, submits or cancels.
Editing is allowed only in `Draft`; cancelling in `Draft`, `Submitted` and `PendingApproval`.

## Approval route

The route is fixed when `FundsReserved` arrives, from the local cost-centre copy at that moment:

1. the cost centre's manager, bound to that person by id;
2. anyone with `finance-approver`, if the amount is strictly over 10,000.00;
3. anyone with `cfo`, if the amount is strictly over 100,000.00.

Steps are taken in order. Whoever may approve the current step may instead reject it, with a reason.

Separation of duties is checked in `Requisition` on every approval and rejection, in this order:

- **Nobody decides their own requisition** (`requisition.self_approval`), whatever roles they hold. A requester
  who is also the CFO still cannot approve their own purchase.
- **Nobody decides two steps of one requisition** (`requisition.duplicate_approver`). A manager who also holds
  `finance-approver` signs the manager step, and someone else signs the finance step. Two signatures means two
  people, or the second one proves nothing.
- **The step has to be yours** (`requisition.not_your_step`): the manager on record for the manager step, the
  role for the others.

The database holds the second rule too: `approval_steps` has a unique index on `(requisition_id, decided_by)`.

`GET /approvals` runs the same three rules as one SQL query, so the list and the approval cannot drift apart
without an integration test noticing.

## Consumers

Every consumer loads the requisition, asks the aggregate whether the fact still applies, and saves only if it
did. A fact that no longer applies is logged and acknowledged, never thrown, because a redelivery or an
overtaken message is normal, not a failure.

| Event | Applies when the requisition is | Otherwise |
|---|---|---|
| `FundsReserved` | `Submitted` | ignored. A cancelled requisition stays cancelled: Budgets saw the cancellation too, releases what it reserved and refuses to reserve again. Any other state means a redelivery. |
| `FundsReservationRejected` | `Submitted` | ignored, for the same reasons |
| `PurchaseOrderIssued` | `Approved`, or `Closed` without an order number yet | ignored. Before `Approved` it cannot happen causally, so a replay must not skip the approvals. After `Closed` the close overtook it: the order number is kept and the status stays `Closed`. |
| `PurchaseOrderClosed` | `Approved` or `Ordered` | ignored. `Approved` counts because an order can be cancelled before it is ever issued. |
| `CostCentreChanged`, `SupplierChanged` | the message's `Version` is higher than the copy's | ignored |

Two copies of one message processed at once both pass the status check, and both try to write. The
requisition's `xmin` makes the second writer fail with a concurrency conflict; MassTransit retries it, it
rereads the requisition, and the aggregate turns it away. Where the write is an insert, the natural keys do the
same: the route's `(requisition_id, sequence)`, and the primary keys of the local copies.

An event for a requisition that does not exist here is logged and dropped: every requisition is committed
before its first event leaves the outbox, so it belongs to another environment or to a restored database.
`FundsReserved` for a cost centre with no local copy is thrown instead, and parked: submitting required that
copy, copies are never deleted, and a broken invariant needs a person.

A supplier status this service does not recognise counts as not active, as `docs/contracts.md` asks.

## Concurrency

The requisition row carries Postgres' `xmin` as its concurrency token. Every change raises `Revision` and
appends one timeline entry numbered by it, so every change writes the requisition's own row, including an
approval that otherwise only touches an approval step. Two approvers acting at once therefore conflict on the
requisition, and the loser gets `concurrency.conflict` rather than a silent second approval.

The local copies carry `xmin` too, so an older snapshot cannot overwrite a newer one it raced.

## Database constraints

On top of primary keys, foreign keys and column precision (money `numeric(18,2)`, quantities `numeric(18,3)`,
unit prices `numeric(18,4)`):

| Table | Constraint | Holds |
|---|---|---|
| `requisitions` | `ck_requisitions_status` | the status is one the domain knows |
| | `ck_requisitions_amount` | the amount is not negative |
| | `ck_requisitions_fiscal_year` | anything past `Draft` has a fiscal year |
| | `ck_requisitions_current_step` | a current step exists exactly while pending approval |
| | `ck_requisitions_rejection_reason` | a rejection, by a person or by Budgets, has a reason |
| | `ck_requisitions_purchase_order` | an ordered requisition knows its order number |
| | unique `number`, unique `serial` | numbers are never reused |
| `requisition_lines` | `ck_requisition_lines_amount` | `amount = round(quantity * unit_price, 2)`, which is `Amounts.Line` since Postgres rounds numeric half away from zero |
| | `ck_requisition_lines_quantity`, `..._unit_price`, `..._line_number` | quantity above zero, price not negative, line 1 to 50 |
| `approval_steps` | unique `(requisition_id, decided_by)` | one person decides at most one step |
| | `ck_approval_steps_manager` | a manager step, and only a manager step, is bound to a person |
| | `ck_approval_steps_decision` | a decided step has a decider and a time, a pending one has neither |
| | `ck_approval_steps_sequence` | steps 1 to 3 |

The timeline's `(requisition_id, sequence)` index is deliberately not unique. The requisition's `xmin` already
serialises writers; a unique index there could fail first, in the same batch, and report a lost race as a
constraint violation instead of a concurrency conflict.

## Decisions the design left open

**One number sequence, with the year written in.** `REQ-2026-000042` is the UTC year of creation and the value
of one Postgres sequence, `requisition_serial`, padded to six digits. A sequence per year would need DDL at
runtime on the first requisition of each January, or a counter row that every insert queues on. The cost is
that numbers do not restart at 1 each year. The gain is a value that rises across years, so it doubles as the
keyset for both lists. Numbers have gaps: a sequence never gives a value back on rollback, and a refused draft
burns one.

**The fiscal year is the UTC year of submission.** That is when Budgets is asked for the money. The needed-by
date was the alternative, and would reserve December's requisition for January in a budget that may not exist
yet.

**Only a draft can be edited.** Budgets has no way to amend a reservation, and the route depends on the amount,
so an edit after submission would leave both wrong. To change a submitted requisition, cancel it and raise a new
one. Allowing it would need a new contract, a `RequisitionAmended` that Budgets re-reserves on.

**A cost centre's manager cannot submit against it** (`requisition.requester_is_manager`). The manager step is
theirs alone and they may not approve their own requisition, so it could never be approved. Refusing at submit
says so before any money is held. Escalating to someone else was the alternative, but it invents a management
hierarchy the design does not have. If the manager changes to the requester between submission and
reservation, the requisition waits until the requester cancels it.

**The manager is fixed with the route.** A later `CostCentreChanged` does not move a pending manager step to
the new manager, because the design fixes the route when funds are reserved. A requisition whose manager has
left waits until it is cancelled.

**Rejection follows the approval rules.** Only whoever could approve the current step may reject it. Letting
any approver reject at any step would give a veto to people the route never asked.

**Who may read a requisition.** Its requester, anyone holding an approval role, and the auditor. Anyone else
is told it does not exist (404), not that it is forbidden, so ids cannot be probed.

**Refusals from the local copies are 409, not 422.** An unknown or inactive cost centre or supplier is about
the copies as they stand now; the missing event may still arrive.

**The snapshot rule lives in the domain.** `CostCentre.Apply` and `Supplier.Apply` take a snapshot only when it
is newer, and property tests shuffle and repeat deliveries against them. A conditional
`UPDATE ... WHERE version < @version` would save a round trip, but it would put the rule in a query only an
integration test can reach, and snapshots change a few times a year.

**`xmin` is the token, `Revision` is not.** The design asks for `xmin`, and it also catches writes that bypass
the domain, such as a manual fix in SQL. `Revision` exists to number the timeline and to make sure every change
writes the row.

**Lines are keyed by `(requisition_id, line_number)`.** An edit updates the lines it keeps, inserts the ones it
adds and deletes the ones it drops, instead of deleting and re-inserting every line.

**Timeline times.** A command is recorded at the service's clock; a fact from another service at its
`OccurredAt`. The timeline is ordered by sequence, the order this service learned things, so a close that
overtook its issue appears before it, which is what happened here.

**Limits.** Quantities, prices and amounts are refused above what their columns hold, and a line's amount is
checked by division before it is multiplied, since the largest quantity times the largest price overflows
`decimal` itself. Text: description 500, unit of measure 16, justification 2,000, rejection reason 1,000.

**Lists.** Keyset pagination on the serial: my requisitions newest first, my approvals oldest first. The
cursor is the serial of the last row; the limit defaults to 50 and is capped at 200.
