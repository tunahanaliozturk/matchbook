# 6. One currency, one rounding rule, and amounts carried in events

Status: accepted

## Context

Five services compute money: a requisition's total, the reservation for it, a purchase order's amount, the
commitment, a matched invoice, a payment. The reconciliation compares them across databases to the cent. Two
services that round a line amount differently will disagree by a cent on some order, and a reconciliation that
has to tolerate a cent of difference cannot tell rounding from a lost update.

Real procurement is multi-currency, with exchange rates at order time, at invoice time and at payment time, and
the differences booked as gains and losses.

## Decision

- **One currency, euros.** Every amount in the system is in the company's functional currency. Suppliers,
  orders, invoices and payments are all in euros, and the pain.001 file says so.
- **One rounding rule**, in `Matchbook.SharedKernel.Amounts`: amounts to two places, half away from zero, as
  invoices round; quantities to three places, unit prices to four. `Amounts.Line(quantity, unitPrice)` is the only
  way any service computes a line amount.
- **Amounts travel in events.** A requisition's total is computed once, by Requisitions, and carried in
  `RequisitionSubmitted`; Budgets reserves exactly that number and never recomputes it. The same holds for
  purchase order amounts and matched invoice amounts. A consumer that needs a total reads it from the event.

## Consequences

- The reconciliation compares amounts for exact equality, and any difference is a defect.
- Foreign-currency suppliers are out of scope, and the limitation is listed in the operations guide. Adding them
  means a currency on every amount, rates from somewhere, and exchange differences in Budgets: a project of its
  own, which is why it is not attempted halfway.
- Postgres stores `numeric` with the same scales, so nothing is lost between C# `decimal` and the database.

## Alternatives considered

**A shared `Money` type.** Tempting, and it would put a type every service's domain depends on into the shared
kernel, which then changes whenever any service needs something new from it. A rounding function and three
constants are enough to agree on.

**Recomputing totals in each consumer from the lines.** Guarantees disagreement the first time one service's
calculation changes.
