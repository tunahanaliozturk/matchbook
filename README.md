# Matchbook

[![ci](https://github.com/tunahanaliozturk/matchbook/actions/workflows/ci.yml/badge.svg)](https://github.com/tunahanaliozturk/matchbook/actions/workflows/ci.yml)
[![licence: MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)

The purchase-to-pay half of an ERP as five .NET 10 microservices: request, approve against a budget, order,
receive, match the invoice three ways, pay.

**Thirty purchases run while RabbitMQ restarts and the two services that hold money are killed with SIGKILL.
Every invoice is still paid exactly once, and the five databases reconcile to the cent on eight cross-service
invariants: 18.8 s from the first request to a clean close at the median of five runs** (Intel Core Ultra 7
255H, Docker Desktop on WSL2; `docs/measurements/chaos.md`, test `ChaosTests`). The same check fails on a
single cent put wrong by hand.

## Quick start

Needs Docker and the .NET 10 SDK.

```
git clone https://github.com/tunahanaliozturk/matchbook.git && cd matchbook
docker compose up -d --build --wait
dotnet run --project tests/Matchbook.SystemTests
```

`docker compose up` builds six images and starts ten containers. The system tests then walk a purchase from
request to payment through the gateway, run the chaos test above, and reconcile after each. CI runs exactly
these commands on every push.

To try it by hand, open the `http/` folder in an editor that runs `.http` files, starting with
`http/suppliers.http`, then `budgets`, `requisitions`, `purchasing` and `payables`. Every seeded user's password
is `matchbook` (the list is in `docs/design.md`). Traces, metrics and logs are in the Aspire dashboard at
http://localhost:18888.

## How a purchase moves

```
rita (requester)     Requisitions ── RequisitionSubmitted ──▶ Budgets        reserve the amount
                     Requisitions ◀── FundsReserved ───────── Budgets
mark (manager)       approves; over 10,000 fiona (finance) too; over 100,000 carl (CFO) too
                     Requisitions ── RequisitionApproved ───▶ Purchasing     draft an order
bruno (buyer)        Purchasing ── PurchaseOrderCommitmentRequested ──▶ Budgets   reservation becomes commitment
                     Purchasing ── PurchaseOrderIssued ─────▶ Payables, Requisitions
rosa (receiver)      Purchasing ── GoodsReceived ───────────▶ Payables
alice (AP clerk)     Payables: order, receipt and invoice agree?  ── InvoiceMatched ──▶ Budgets, Purchasing
tess, then trevor    Payables: payment run drafted by one treasurer, released by another, pain.001 file
```

No service calls another over HTTP. Each owns one Postgres database, publishes through a transactional outbox and
consumes through an inbox, and the gateway (YARP) is the only way in. Separation of duties is enforced in the
domain, not the UI: nobody approves their own requisition, the receiver cannot be the buyer who issued the order,
and a bank account change, a supplier activation and a payment run each need a second person.

## The decision worth arguing with

**Consumers never assume order, and they never throw because something has not arrived yet** (ADR 0002). There
is no saga orchestrator. RabbitMQ keeps one queue in order, but each service reads several queues with sixteen
messages in flight and retries put messages behind later ones, so `GoodsReceived` can reach Payables before the
order it belongs to, and `RequisitionCancelled` can reach Budgets before the `RequisitionSubmitted` it cancels.
Every consumer stores what it was told and acts once the rest is present: Payables keeps the receipt and matches
when the order arrives; Budgets leaves a tombstone for the cancellation and refuses the late reservation.

What that buys: no coordinator to fail, and each service can be down without stopping the others, which the
chaos test shows. What it costs: the process is not written down in one place in code, only in
`docs/design.md`, and proving it correct takes a reconciler that reads five databases and checks the invariants
no single service can see (`tests/Matchbook.Stack/Reconciliation.cs`). A reviewer who prefers an orchestrator
has a fair case; ADR 0002 names it and says why it lost here.

## What is proved, and where

| Claim | Evidence |
|---|---|
| Two hundred simultaneous reservations against room for fifty grant exactly fifty | `Two_hundred_simultaneous_reservations_against_room_for_fifty_grant_exactly_fifty` (Budgets) |
| An invoice is paid at most once, however the runs race | `Two_treasurers_drafting_at_the_same_moment_never_put_one_invoice_in_both_runs`, `Releasing_a_run_twice_at_once_pays_each_invoice_once_and_the_file_carries_exactly_those_payments` (Payables) |
| A duplicate invoice number is caught even when two clerks capture it at once | `Two_captures_of_one_number_at_the_same_moment_create_one_invoice` (Payables) |
| IBANs never appear in plain text at rest, in an outbox or on the broker | ADR 0007, and the Suppliers and Payables integration tests that read the raw columns and messages |
| Money reconciles across all five databases after restarts and kills | `ChaosTests`, `docs/measurements/chaos.md` |
| A 5,000 invoice payment run drafts in 1.5 to 2.4 s and releases in 2.0 to 3.1 s | `PaymentRunScaleTests`, `docs/services/payables.md` |
| No project references another service's projects, and Domain references nothing but the shared kernel | `tests/Matchbook.ArchitectureTests` |

Against real Postgres 18 and RabbitMQ 4.3 in containers throughout; there is no in-memory database or transport
anywhere in the tests. 666 unit tests, 204 integration tests, 30 architecture tests and 2 system tests.

## Stack

.NET 10 and C# 14, ASP.NET Core minimal APIs, EF Core 10 on Npgsql, MassTransit 8.5 with the Entity Framework
outbox and inbox, RabbitMQ, YARP, Keycloak, OpenTelemetry to the Aspire dashboard. xUnit v3 with Shouldly and
FsCheck, Testcontainers. MassTransit is pinned to 8.5 because version 9 is commercially licensed; a licence
audit in CI (`tools/Matchbook.LicenseAudit`) fails the build on any package, at any depth, whose licence would
cost a commercial user money.

## Further reading

- `docs/design.md`: the contract the services are built to, including every cross-service invariant.
- `docs/adr/`: seven decisions, each with the alternatives that lost.
- `docs/services/`: one document per service: states, rules, what the database enforces, metrics, and the
  decisions its author made.
- `docs/operations.md`: configuration, what to watch, a runbook per anticipated failure, known limitations.
- `docs/contracts.md`: the integration events, which a test pins by name and shape.

## Known limitations

The full list is in `docs/operations.md`. The ones a reviewer is most likely to hit first:

- The outbox delivers at least once. A reply can reach the broker twice under one message id; every consumer's
  inbox drops the copy, but anything else listening to the broker has to do the same.
- The reconciler runs only in the tests, not on a schedule.
- One currency, one replica per service tested, migrations run at startup.

## Licence

MIT. See `LICENSE`.
