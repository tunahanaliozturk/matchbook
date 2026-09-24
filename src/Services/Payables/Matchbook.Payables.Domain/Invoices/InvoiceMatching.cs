using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>Matches several invoices on one order, one after another, each against what the ones before it took.</summary>
public static class InvoiceMatching
{
    /// <summary>
    /// Evaluates the invoices oldest capture first, adding each one that matches to <paramref name="position"/>
    /// before the next is looked at, so between them they never bill more than was received. Returns the invoices
    /// that matched.
    /// </summary>
    public static IReadOnlyList<Invoice> Evaluate(
        IEnumerable<Invoice> invoices,
        PurchaseOrder? order,
        OrderPosition position,
        int? paymentTermsDays,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(position);

        var matched = new List<Invoice>();
        foreach (Invoice invoice in invoices.OrderBy(invoice => invoice.CapturedAt).ThenBy(invoice => invoice.Id))
        {
            if (invoice.Evaluate(order, position, paymentTermsDays, now).Outcome == MatchOutcome.Matched)
            {
                position.RecordMatched(invoice);
                matched.Add(invoice);
            }
        }

        return matched;
    }
}
