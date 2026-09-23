# 2. Choreography, with consumers that tolerate any order

Status: accepted

## Context

A purchase takes days and involves a dozen people: a requester, up to three approvers, a buyer, a receiver, an AP
clerk and approver, two treasurers. Each step belongs to one service and one person. Something has to decide what
happens after each step.

RabbitMQ delivers each queue in order, but a service consumes several queues, each with up to sixteen messages in
flight, and a retry puts a message behind ones that arrived after it. So two events about the same document can
arrive in either order: `RequisitionCancelled` before `RequisitionSubmitted` at Budgets, `GoodsReceived` before
`PurchaseOrderIssued` at Payables, `InvoiceMatched` after `PurchaseOrderClosed`.

## Decision

**No orchestrator.** Each service owns the state of its own documents and reacts to events from the others. The
process is the sum of those reactions, drawn once in `docs/design.md`.

**Every consumer is correct under any order and any number of deliveries.** Four techniques cover every case in
the system:

1. **Versioned snapshots** for reference data. `SupplierChanged` and `CostCentreChanged` carry the whole state and
   a version; a consumer applies a message only if its version is newer than the one it holds. Order and
   duplicates stop mattering.
2. **Facts stored as they arrive, decisions taken when complete.** Payables stores a receipt for an order it has
   not heard of, and matches invoices when the order arrives. No consumer throws because something has not
   arrived yet, so nothing waits in a retry loop for a message that is stuck behind it.
3. **Tombstones** for cancellations. When Budgets releases a requisition's reservation, it records the release even
   if there was nothing to release, and refuses a reservation for that requisition afterwards. A late
   `RequisitionSubmitted` cannot resurrect money for a cancelled request.
4. **Replies that say what they answer.** A commitment request carries an attempt number and the reply echoes
   it, so Purchasing ignores a rejection of attempt 1 that arrives after attempt 2 succeeded.

Where the outcome of two events has to be the same in either order, the arithmetic is written to commute. Budgets
relieves a commitment by `min(invoice, remaining)` and releases what remains when the order closes; both orders end
with the same commitment (zero) and the same actual. Budgets' property tests check exactly that.

## Consequences

- No single place shows where a purchase is. Each service answers for its own part, and the console assembles
  the picture. The system tests follow a purchase through all five.
- Adding a step means changing the services on either side of it, not one orchestrator. For a process that
  changes rarely and whose steps are owned by different teams, that is the right way round.
- The design is harder to get right than a sequence of commands, and the property and integration tests exist
  because of that.

## Alternatives considered

**A saga orchestrator** (a MassTransit state machine) that sends commands and waits for replies. It makes the
process visible in one class, and the sibling repository `order-saga-system` shows one. Here most steps wait on a
person for hours or days, each step already has an obvious owner, and an orchestrator would hold a second copy of
every document's state. It would also not remove the reordering problem, only move it into one place.

**Partitioned, strictly ordered delivery** (Kafka keyed by document, or MassTransit's partitioner). Narrows the
problem without removing it: a retry still reorders, and events about one purchase come from four services on
four streams. Tolerating order is cheaper than enforcing it.
