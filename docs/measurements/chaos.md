# Thirty purchases through a broker restart and two killed services

Measured 2026-09-24 by `Money_reconciles_across_five_databases_after_a_broker_restart_and_two_killed_services`
in `tests/Matchbook.SystemTests/ChaosTests.cs`, five runs back to back against one compose stack.

**Host**: Intel Core Ultra 7 255H (16 cores), 31 GB RAM, Windows 11, Docker Desktop on WSL2 with 16 CPUs and
16 GB given to the Linux VM. Every container, the test process and Keycloak share that one machine.

**Command**, with the stack already up from the README quick start:

```
dotnet run --project tests/Matchbook.SystemTests -c Release -- -class Matchbook.SystemTests.ChaosTests -showliveoutput
```

## What the test does

Thirty purchases start half a second apart, each taken from requisition to a matched invoice by the people who
own the steps (rita, mark, bruno, rosa, alice). While they run:

1. at 2 s, `docker restart` on RabbitMQ;
2. straight after, `docker kill --signal SIGKILL` on Budgets, which is started again 3 s later;
3. then the same for Payables.

Once every invoice is payable, tess drafts a payment run and trevor releases it. The test then waits until every
outbox table is empty and every working queue is drained, reads all five databases through a read-only role and
checks the eight invariants in `tests/Matchbook.Stack/Reconciliation.cs`.

## Results

Seconds from the first purchase starting.

| Run | Broker back | Budgets killed | Payables killed | All containers up | 30 invoices payable | Quiet and reconciled | Discrepancies |
|---|---|---|---|---|---|---|---|
| 1 | 3.7 | 4.1 | 7.8 | 11.2 | 15.7 | 18.8 | 0 |
| 2 | 3.7 | 4.1 | 7.8 | 11.2 | 15.2 | 23.6 | 0 |
| 3 | 3.7 | 4.1 | 7.9 | 11.2 | 15.0 | 18.5 | 0 |
| 4 | 3.7 | 4.0 | 7.8 | 11.3 | 15.1 | 18.5 | 0 |
| 5 | 3.7 | 4.1 | 7.9 | 11.2 | 21.3 | 23.0 | 0 |

Median 18.8 s from the first request to a reconciled close, slowest 23.6 s. No message was parked on an error
queue in any run; the test fails if one is.

The last purchase starts at 14.5 s, so "30 invoices payable" at about 15 s means the stack had caught up with
everything the outages delayed within a second of the last request. Runs 2 and 5 are the outliers. The run log
does not say which step waited, and the test does not trace it yet, so the cause is not known.

## What this does not show

- Postgres was never restarted. Every service needs its database to answer anything, so a Postgres outage is
  plain downtime, and the outbox has nothing to add there.
- One replica per service. Killing one of two replicas would test the gateway and the competing consumers,
  which this does not.
- Thirty purchases is a correctness load, not a throughput one. The gateway's per-caller limit (200 requests a
  second) is the ceiling the test actually meets, because thirty purchases poll as the same five people.

## Proving the check can fail

After the runs above, one budget's `actual` was raised by 0.01 directly in the `budgets` database and the end to
end test run again. It failed on two invariants, "budget figures equal the ledger" and "actual equals matched
invoices", each naming the budget and both figures. The value was then put back.
