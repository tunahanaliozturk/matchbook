# 1. MassTransit 8 on RabbitMQ, with the Entity Framework outbox and inbox

Status: accepted

## Context

Every step of a purchase crosses a service boundary: a submitted requisition has to reach Budgets, a reservation
has to come back, an issued order has to reach Payables. Each service commits to its own database and then tells
the others. Two failures have to be impossible:

1. The change commits and the event is lost (the process dies between the commit and the publish). Budgets never
   hears of the requisition, and the requester waits forever.
2. The event is published and the change does not commit (the publish succeeds, then the transaction rolls
   back). Budgets reserves money for a requisition that does not exist.

And one has to be harmless: the broker delivering a message twice, which at-least-once delivery guarantees will
happen.

## Decision

MassTransit 8.5 on RabbitMQ, with its Entity Framework outbox and inbox on each service's own DbContext,
configured once in `BuildingBlocks` (`AddMatchbookMessaging`).

- **Outbox.** An API handler publishes through `IEventPublisher`; the message is written to the service's
  `outbox_message` table by the same `SaveChangesAsync` that commits the change, and a background sender delivers
  it. Inside a consumer, publishes are held until the consumer finishes and are committed with its changes. An
  event leaves if and only if its cause committed.
- **Inbox.** Each consumer's receive endpoint records the message ids it has processed, in the same transaction
  as its effects, and skips a redelivery within the deduplication window (an hour).
- **Idempotency by natural key as well.** The window is finite and a message can be republished under a new id
  by a retry upstream, so every consumer is also idempotent on its business keys: a unique index on (document,
  step) in Budgets' ledger, one purchase order per requisition in Purchasing, receipts by receipt id in Payables.
  The inbox makes duplicates cheap; the keys make them harmless.
- **Retries** happen in memory, outside the outbox, so each attempt gets a fresh transaction. After five attempts
  over about fifteen seconds the message goes to the endpoint's `_error` queue. A business refusal is never an
  exception; it is an event (`FundsReservationRejected`), and the consumer succeeds.

`tests/Matchbook.BuildingBlocks.Tests` proves the three properties against real Postgres and RabbitMQ: an event
published in a request that fails before commit never arrives, one that commits does, and a message delivered
twice under one id is handled once.

## Why MassTransit 8, not 9

Version 9 moved to a commercial licence. 8.5.10 is Apache 2.0, targets .NET 10 and builds against EF Core 10, and
the outbox and inbox are the parts this project needs. The licence audit in CI reads the licence of every package
in the tree and fails the build if a 9.x ever arrives transitively.

## Consequences

- Every service database carries three MassTransit tables (`inbox_state`, `outbox_message`, `outbox_state`),
  created by its own migration.
- Delivery is at least once and in no guaranteed order across queues. That is the premise of ADR 0002.
- The outbox adds a polling delay (100 ms) between commit and publish. It shows in the end-to-end latency
  numbers and is small next to a human approval.
- The outbox needs a transaction around every consumer, so the database connection cannot use a retrying
  execution strategy. A failed attempt is retried whole by the bus instead.

## Alternatives considered

**A hand-rolled outbox.** Done once already in this portfolio (`order-saga-system`), and it is the right way to
learn what an outbox is. Here the interesting part is the domain, and a library with years of production use is
the better place to spend less attention.

**Publishing after commit, and hoping.** Loses events on every crash between the two lines.

**Change data capture from each database** (Debezium, or `walrus` from this portfolio). No outbox table in the
application at all, but the events become the shape of the tables, and every consumer learns another service's
schema. The outbox keeps the contract explicit.

**Kafka.** Partitioned ordering per key would remove some of the reordering this system tolerates, at the price of
a heavier broker and a partitioning decision for every event. RabbitMQ with order-tolerant consumers is simpler to
run and makes the tolerance a tested property rather than a deployment assumption.
