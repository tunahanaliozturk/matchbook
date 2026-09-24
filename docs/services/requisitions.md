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

## API

The gateway routes `/api/requisitions` and `/api/approvals` here with `/api` removed. Every endpoint needs a
token; the roles below open the door, and the domain then decides whether this person may do this to this
requisition. Every command answers with the requisition's new state. `http/requisitions.http` walks through
them in order.

| Method and path | Roles | Answers |
|---|---|---|
| `POST /requisitions` | requester | 201, the draft. Idempotent on an optional `id` in the body. |
| `GET /requisitions?after=&limit=` | requester, approver, finance-approver, cfo, auditor | 200, a page, newest first: a requester's own, everyone's for the others |
| `GET /requisitions/{id}` | as above | 200, with lines, route and timeline; 404 for a requisition the caller may not read |
| `PUT /requisitions/{id}` | requester | 200, the edited draft |
| `POST /requisitions/{id}/submit` | requester | 200; publishes `RequisitionSubmitted` |
| `POST /requisitions/{id}/cancel` | requester | 200; publishes `RequisitionCancelled` unless it was a draft |
| `POST /requisitions/{id}/approve` | approver, finance-approver, cfo | 200; the last step publishes `RequisitionApproved` |
| `POST /requisitions/{id}/reject` | approver, finance-approver, cfo | 200, with `{ "reason": ... }`; publishes `RequisitionRejected` |
| `GET /approvals?after=&limit=` | approver, finance-approver, cfo | 200, a page of what the caller can decide now, longest waiting first |

A missing field is a 400 naming it. A broken rule is a problem with its code: 422 for content
(`requisition.line_count_invalid`), 409 for state (`requisition.not_draft`, `concurrency.conflict`), 403 for
who (`requisition.self_approval`), 404 for what is not there or not yours to see. The OpenAPI document at
`/openapi/v1.json` (through the gateway, `/openapi/requisitions.json`) lists every endpoint with its body,
its answer and the problems it can return.

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

| Event | Queue | Applies when the requisition is | Otherwise |
|---|---|---|---|
| `FundsReserved` | `requisitions-funds-reserved` | `Submitted` | ignored. A cancelled requisition stays cancelled: Budgets saw the cancellation too, releases what it reserved and refuses to reserve again. Any other state means a redelivery. |
| `FundsReservationRejected` | `requisitions-funds-reservation-rejected` | `Submitted` | ignored, for the same reasons |
| `PurchaseOrderIssued` | `requisitions-purchase-order-issued` | `Approved`, or `Closed` without an order number yet | ignored. Before `Approved` it cannot happen causally, so a replay must not skip the approvals. After `Closed` the close overtook it: the order number is kept and the status stays `Closed`. |
| `PurchaseOrderClosed` | `requisitions-purchase-order-closed` | `Approved` or `Ordered` | ignored. `Approved` counts because an order can be cancelled before it is ever issued. |
| `CostCentreChanged` | `requisitions-cost-centre-changed` | the message's `Version` is higher than the copy's | ignored |
| `SupplierChanged` | `requisitions-supplier-changed` | the message's `Version` is higher than the copy's | ignored |

The consumers in `Infrastructure/Messaging` are adapters that call the Application handler and nothing else.
The outbox and inbox around them, the retries and the error queues come from `AddMatchbookMessaging`.

Two copies of one message processed at once both pass the status check, and both try to write. The
requisition's `xmin` makes the second writer fail with a concurrency conflict; MassTransit retries it, it
rereads the requisition, and the aggregate turns it away. Where the write is an insert, the natural keys do the
same: the route's `(requisition_id, sequence)`, and the primary keys of the local copies.

An event for a requisition that does not exist here is logged and dropped: every requisition is committed
before its first event leaves the outbox, so it belongs to another environment or to a restored database.
`FundsReserved` for a cost centre with no local copy throws instead: submitting required that copy, copies are
never deleted, and a broken invariant needs a person. It is retried five times over about fifteen seconds and
then parked on `requisitions-funds-reserved_error`.

A supplier status this service does not recognise counts as not active, as `docs/contracts.md` asks.

## Concurrency

The requisition row carries Postgres' `xmin` as its concurrency token. Every change raises `Revision` and
appends one timeline entry numbered by it, so every change writes the requisition's own row, including an
approval that otherwise only touches an approval step. Two approvers acting at once therefore conflict on the
requisition, and the loser gets `concurrency.conflict` rather than a silent second approval.

The local copies carry `xmin` too, so an older snapshot cannot overwrite a newer one it raced.

## Metrics

The meter `Matchbook.Requisitions`, exported with every other `Matchbook.*` meter. Each measurement is taken
after the change that caused it commits, so a lost race or a failed save is never counted.

| Instrument | Kind | Tags | Counts |
|---|---|---|---|
| `matchbook.requisitions.submitted` | counter | | requisitions sent to Budgets |
| `matchbook.requisitions.approved` | counter | `route.steps` | requisitions whose last step was signed |
| `matchbook.requisitions.rejected` | counter | | requisitions an approver rejected |
| `matchbook.requisitions.budget_rejected` | counter | `reason` | requisitions Budgets would not reserve for |
| `matchbook.requisitions.time_to_approval` | histogram, seconds | `route.steps` | submission to last approval |

The histogram's buckets run from a minute to a fortnight, since approvals wait on people. Split by route length,
it shows whether the finance and CFO steps are where requisitions wait.

## Database constraints

On top of primary keys, foreign keys and column precision (money `numeric(18,2)`, quantities `numeric(18,3)`,
unit prices `numeric(18,4)`):

| Table | Constraint | Holds |
|---|---|---|
| `requisitions` | `ck_requisitions_status` | the status is one the domain knows |
| | `ck_requisitions_amount` | the amount is not negative |
| | `ck_requisitions_fiscal_year` | anything that was submitted has a fiscal year (a draft, or a draft cancelled before submission, has none) |
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

The first version of `ck_requisitions_fiscal_year` said "anything past `Draft`", which refused a draft
cancelled before submission. The integration test that cancels a draft found it; the second migration
(`CancelledDraftFiscalYear`) corrects it rather than editing the first.

## Tests

The unit tests cover the domain, with FsCheck properties for routing, separation of duties, amounts and delivery
order. The integration tests run the real host against Postgres 18 and RabbitMQ 4.3 in containers, one database
and virtual host per test class, with a probe that plays the other services over the broker. They prove:

- **The route end to end.** Rita raises and submits over HTTP, `RequisitionSubmitted` reaches the probe, the
  probe answers `FundsReserved`, and mark, fiona and carl approve over HTTP. At 9,999.99 and 10,000.00 only the
  manager signs; at 10,000.01, 99,999.99 and 100,000.00 finance signs too; at 100,000.01 the CFO as well. The
  last signature publishes `RequisitionApproved` with both lines, the amount and the approvers in route order.
- **Separation of duties over HTTP**, each with its status and code: approving your own requisition, signing
  two steps of one, a manager who is not the manager on record, a manager raising against their own cost centre.
- **`GET /approvals`** shows mark, maya, fiona, a second finance approver and carl exactly the requisitions
  waiting on them, never a draft, one Budgets has not answered, or a cancelled one, and drops a requisition from
  every queue once decided. It pages from the longest waiting.
- **The race.** Two finance approvers decide one step at once: one 200, one 409 `concurrency.conflict`, and one
  `RequisitionApproved`. The test holds the row with `SELECT ... FOR UPDATE` until Postgres shows both requests
  waiting on locks, then lets go, so the two really read the same version. Without that the requests could run
  one after the other and the test would pass without proving anything.
- **Order and repetition with real messages.** A reservation after a cancel is ignored; a refusal ends the
  requisition and a late reservation does not revive it; a repeated reservation builds no second route; cost
  centre and supplier snapshots out of order keep the newest; an unknown supplier status is not active; issue
  then close gives `Ordered` then `Closed`; a close that overtakes its issue still keeps the order number; an
  issue replayed before approval skips nothing; repeats change nothing, down to the revision. Repeats go out
  under new message ids, which the inbox cannot catch, so it is the requisition's own state that holds.
- **The HTTP surface.** Idempotent creates (a repeat, a race of two, a reused id with other content or from
  someone else), 401 without a token, 403 for the wrong role, the auditor reading everything and changing
  nothing, 404 for someone else's requisition, 400 naming a missing field, 422 with a code for a broken rule,
  and an OpenAPI document that describes every endpoint with its body and problems.
- **Metrics**, read with a `MeterListener`, and the two database constraints above, by writing past the domain.

`dotnet run --project tests/Services/Requisitions/Matchbook.Requisitions.IntegrationTests`: 40 tests, about a
minute once the images are pulled.

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

**Lists.** Keyset pagination on the serial: requisitions newest first, approvals oldest first. The cursor is the
serial of the last row. The limit defaults to 50 and is clamped to 1 to 200 rather than refused, which is what
"capped" asks for; .NET 10 does not run validation attributes on a nullable number in the query string anyway.

## Decisions made wiring the service

**Decisions live on the requisition.** Approve and reject are `POST /requisitions/{id}/approve` and `/reject`;
`/approvals` is only the caller's queue. A single `POST /approvals/{id}` with the decision in the body was the
alternative. Two URLs say what they do in the OpenAPI document and in access logs, and a rejection's body
(the reason) is not optional baggage on an approval.

**One list, scoped by the read rule.** `GET /requisitions` returns what the caller may read: a requester's own,
everyone's for approvers and the auditor. The same `Requisition.SeesEveryRequisition` decides both the list and
a single read. A separate "mine" and "all" endpoint was the alternative: two lists of the same thing, and the
rule written twice.

**An idempotent create compares, it does not remember.** A create with an id that exists is answered with that
requisition when the request describes it as it stands (`Requisition.IsRepeatOf`: same requester, same content
read the way drafting reads it), and with 409 `request.id_reused` otherwise, including when the id is someone
else's, so the answer reveals nothing. Two creates racing with one id meet at the primary key: the loser's save
fails, it rereads, and it answers like a repeat. Storing a hash of each create request, or its response, was the
alternative; that is a column and a second definition of "the same request", to cover a retry after the
requisition was edited, and a client cannot edit what it never got an answer for.

**Responses are the Application's views.** `RequisitionView`, `RequisitionSummary` and `Page<T>` already exist
to be read by someone outside the domain, so the API returns them rather than copying each into an identical
Api record. Requests are Api records, because they carry the HTTP shape rules and the optional id. Enums travel
by name, so renaming a status is a breaking change; the OpenAPI document pins the names.

**Shape is a 400, content is a 422.** Request records only mark what must be present. Counts, scales, lengths
and dates are the domain's, answered with a code a client can branch on. Checking them twice would give two
answers for one mistake.

**Metrics are counted by the handlers, on a meter the host creates.** Infrastructure creates the `Meter` from
`IMeterFactory`, which Application may not reference, and hands it to `RequisitionMetrics`, whose instruments
the handlers record after a successful save. An EF interceptor that watched status columns change was the
alternative: nothing in the handlers, but business events read back out of row diffs.

**The one-decision index answers like the domain, with a 409.** `MapUniqueViolation` turns a violation of
`ix_approval_steps_requisition_id_decided_by` into `requisition.duplicate_approver`. The domain refuses first
(403), so the index only speaks if two saves race past that check, and then it is a conflict.

**`Location` is the service's own path.** A create answers `Location: /requisitions/{id}`. Behind the gateway
the public path has `/api` in front, which this service cannot know. A relative `requisitions/{id}` would
resolve correctly for `POST /api/requisitions`, and wrongly for `POST /api/requisitions/`.
