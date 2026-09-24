namespace Matchbook.Stack;

/// <summary>One broken invariant, about one document.</summary>
public sealed record Discrepancy(string Invariant, string Subject, string Detail)
{
    public override string ToString() => $"{Invariant}: {Subject}: {Detail}";
}

/// <summary>
/// The checks a finance team runs at month end, across five databases: does every subledger agree with the
/// others. They are the invariants in docs/design.md, "What must hold across services", written as code. Each
/// returns the documents that break it; an empty list is a clean close.
/// </summary>
public static class Reconciliation
{
    public static readonly string[] Invariants =
    [
        "budget figures equal the ledger",
        "no grant ever overdrew a budget",
        "reserved equals open requisitions",
        "committed equals open orders less what was matched",
        "actual equals matched invoices",
        "every invoice paid at most once, for its amount",
        "paid to the account in force at release",
        "received and invoiced agree across services",
    ];

    public static IReadOnlyList<Discrepancy> Check(Ledgers ledgers)
    {
        ArgumentNullException.ThrowIfNull(ledgers);

        return
        [
            .. BudgetsEqualTheirLedger(ledgers),
            .. NoGrantOverdrew(ledgers),
            .. ReservationsFollowRequisitions(ledgers),
            .. CommitmentsFollowOrders(ledgers),
            .. ActualsEqualMatchedInvoices(ledgers),
            .. PaidAtMostOnce(ledgers),
            .. PaidToTheAccountInForce(ledgers),
            .. QuantitiesAgree(ledgers),
        ];
    }

    private static IEnumerable<Discrepancy> BudgetsEqualTheirLedger(Ledgers l)
    {
        foreach ((Guid id, BudgetRow budget) in l.Budgets)
        {
            var sums = l.LedgerSums.GetValueOrDefault(id);

            if (budget.Allotted != sums.Allotted || budget.Reserved != sums.Reserved
                || budget.Committed != sums.Committed || budget.Actual != sums.Actual)
            {
                yield return new(Invariants[0], $"budget {id}", $"figures {budget} but ledger sums {sums}");
            }

            decimal held = l.Reservations.Values.Where(r => r.Status == "Held" && r.Budget == id).Sum(static r => r.Amount);

            if (budget.Reserved != held)
            {
                yield return new(Invariants[0], $"budget {id}", $"reserved {budget.Reserved} but held reservations sum to {held}");
            }

            decimal remaining = l.Commitments.Values.Where(c => c.Status == "Committed" && c.Budget == id).Sum(static c => c.Remaining);

            if (budget.Committed != remaining)
            {
                yield return new(Invariants[0], $"budget {id}", $"committed {budget.Committed} but open commitments sum to {remaining}");
            }
        }
    }

    private static IEnumerable<Discrepancy> NoGrantOverdrew(Ledgers l) =>
        l.OverdrawnGrants.Select(grant => new Discrepancy(
            Invariants[1], $"budget {grant.Budget}", $"{grant.Step} entry {grant.Sequence} left {grant.Available} available"));

    private static IEnumerable<Discrepancy> ReservationsFollowRequisitions(Ledgers l)
    {
        foreach ((Guid id, (string status, decimal amount)) in l.Requisitions)
        {
            bool known = l.Reservations.TryGetValue(id, out var reservation);

            string[] expected = status switch
            {
                "Draft" => ["none"],
                "Submitted" => ["stuck"],
                "PendingApproval" or "Approved" => ["Held"],
                "BudgetRejected" => ["Refused"],
                "Ordered" => ["Committed"],
                "Closed" => ["Committed", "Released"],
                "Rejected" => ["Released"],
                "Cancelled" => ["none", "Released"],
                _ => [$"unknown status {status}"],
            };

            string actual = known ? reservation.Status : "none";

            if (!expected.Contains(actual))
            {
                yield return new(Invariants[2], $"requisition {id}", $"is {status} but its reservation is {actual}");
            }
            else if (actual == "Held" && reservation.Amount != amount)
            {
                yield return new(Invariants[2], $"requisition {id}", $"amount {amount} but {reservation.Amount} is held");
            }
        }

        foreach ((Guid id, var reservation) in l.Reservations)
        {
            if (reservation.Status == "Held" && !l.Requisitions.ContainsKey(id))
            {
                yield return new(Invariants[2], $"requisition {id}", "money is held for a requisition Requisitions does not have");
            }
        }
    }

    private static IEnumerable<Discrepancy> CommitmentsFollowOrders(Ledgers l)
    {
        Dictionary<Guid, decimal> matched = MatchedTotals(l);

        foreach ((Guid id, (Guid _, string status, decimal amount)) in l.Orders)
        {
            CommitmentRow? commitment = l.Commitments.GetValueOrDefault(id);
            string state = commitment?.Status ?? "none";

            switch (status)
            {
                case "Issued":
                    decimal open = Math.Max(0m, amount - matched.GetValueOrDefault(id));

                    if (commitment is not { Status: "Committed" } || commitment.Amount != amount || commitment.Remaining != open)
                    {
                        yield return new(Invariants[3], $"order {id}",
                            $"issued for {amount} with {matched.GetValueOrDefault(id)} matched, but the commitment is {commitment?.ToString() ?? "missing"}");
                    }

                    break;

                case "Completed" or "ShortClosed" when state != "Closed" || commitment!.Remaining != 0m:
                    yield return new(Invariants[3], $"order {id}", $"is {status} but the commitment is {commitment?.ToString() ?? "missing"}");
                    break;

                case "Draft" or "CommitmentPending" or "Cancelled" when state == "Committed":
                    yield return new(Invariants[3], $"order {id}", $"is {status} but money is committed to it");
                    break;
            }
        }
    }

    private static IEnumerable<Discrepancy> ActualsEqualMatchedInvoices(Ledgers l)
    {
        Dictionary<Guid, decimal> matched = MatchedTotals(l);
        Dictionary<Guid, decimal> expected = [];

        foreach ((Guid order, decimal total) in matched)
        {
            if (l.Commitments.GetValueOrDefault(order)?.Budget is { } budget)
            {
                expected[budget] = expected.GetValueOrDefault(budget) + total;
            }
            else
            {
                yield return new(Invariants[4], $"order {order}", $"{total} matched against an order Budgets never committed");
            }
        }

        foreach ((Guid id, BudgetRow budget) in l.Budgets)
        {
            if (budget.Actual != expected.GetValueOrDefault(id))
            {
                yield return new(Invariants[4], $"budget {id}", $"actual {budget.Actual} but matched invoices sum to {expected.GetValueOrDefault(id)}");
            }
        }
    }

    private static IEnumerable<Discrepancy> PaidAtMostOnce(Ledgers l)
    {
        foreach (IGrouping<Guid, PaymentRow> payments in l.Payments.Where(static p => p.ItemStatus == "Paid").GroupBy(static p => p.Invoice))
        {
            if (payments.Count() > 1)
            {
                yield return new(Invariants[5], $"invoice {payments.Key}", $"paid {payments.Count()} times, by runs {string.Join(", ", payments.Select(static p => p.Run))}");
            }

            InvoiceRow? invoice = l.Invoices.GetValueOrDefault(payments.Key);

            if (invoice is null || invoice.Status != "Paid" || payments.First().Amount != invoice.Total)
            {
                yield return new(Invariants[5], $"invoice {payments.Key}", $"paid {payments.First().Amount} but the invoice is {invoice?.ToString() ?? "missing"}");
            }

            if (payments.Any(static p => p.RunStatus != "Released"))
            {
                yield return new(Invariants[5], $"invoice {payments.Key}", "marked paid by a run that was never released");
            }
        }

        foreach ((Guid id, InvoiceRow invoice) in l.Invoices.Where(static pair => pair.Value.Status == "Paid"))
        {
            if (!l.Payments.Any(p => p.Invoice == id && p.ItemStatus == "Paid"))
            {
                yield return new(Invariants[5], $"invoice {id}", "is paid but no released run paid it");
            }
        }
    }

    private static IEnumerable<Discrepancy> PaidToTheAccountInForce(Ledgers l)
    {
        foreach (PaymentRow payment in l.Payments.Where(static p => p.ItemStatus == "Paid"))
        {
            if (payment.ReleasedAt is not { } releasedAt)
            {
                continue;
            }

            int? inForce = l.ApprovedAccounts.GetValueOrDefault(payment.Supplier)?
                .Where(account => account.ApprovedAt <= releasedAt)
                .Select(static account => (int?)account.Version)
                .LastOrDefault();

            if (inForce != payment.AccountVersion)
            {
                yield return new(Invariants[6], $"invoice {payment.Invoice}",
                    $"paid to account version {payment.AccountVersion}, but version {inForce?.ToString() ?? "none"} was in force at release");
            }
        }
    }

    private static IEnumerable<Discrepancy> QuantitiesAgree(Ledgers l)
    {
        foreach (((Guid order, int line), (decimal ordered, decimal received, decimal invoiced)) in l.OrderLines)
        {
            if (!(invoiced <= received && received <= ordered))
            {
                yield return new(Invariants[7], $"order {order} line {line}", $"ordered {ordered}, received {received}, invoiced {invoiced}");
            }

            decimal payablesReceived = l.PayablesReceived.GetValueOrDefault((order, line));
            decimal payablesInvoiced = l.PayablesInvoiced.GetValueOrDefault((order, line));

            if (payablesReceived != received || payablesInvoiced != invoiced)
            {
                yield return new(Invariants[7], $"order {order} line {line}",
                    $"Purchasing has received {received} and invoiced {invoiced}; Payables has {payablesReceived} and {payablesInvoiced}");
            }
        }

        foreach ((Guid id, InvoiceRow invoice) in l.Invoices.Where(static pair => pair.Value.Matched))
        {
            if (!l.InvoicesPurchasingSawMatched.Contains(id))
            {
                yield return new(Invariants[7], $"invoice {id}", $"matched in Payables but not counted by Purchasing on order {invoice.Order}");
            }
        }
    }

    private static Dictionary<Guid, decimal> MatchedTotals(Ledgers l) =>
        l.Invoices.Values
            .Where(static invoice => invoice.Matched && invoice.Status is "Payable" or "Scheduled" or "Paid")
            .GroupBy(static invoice => invoice.Order)
            .ToDictionary(static group => group.Key, static group => group.Sum(static invoice => invoice.Total));
}
