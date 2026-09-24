using System.Globalization;
using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>
/// Matches an invoice against its purchase order and the goods received on it. A pure function: the same invoice,
/// order and position always give the same result, and it changes nothing.
/// </summary>
/// <remarks>
/// The checks run in a fixed order and the first that fails decides:
/// <list type="number">
/// <item>the order was cancelled: rejected (checked first, because a cancellation can arrive for an order Payables never saw issued);</item>
/// <item>the order has not arrived: wait for it;</item>
/// <item>the order is with another supplier, or lacks a billed line: rejected;</item>
/// <item>a line bills more, together with earlier matched invoices, than was received: wait for goods;</item>
/// <item>a line's price is outside tolerance and no approver accepted it: a price variance exception;</item>
/// <item>otherwise matched.</item>
/// </list>
/// Quantity comes before price on purpose. A shortfall resolves itself when goods arrive, so an approver is only
/// asked about a price once it is the one thing standing between the invoice and payment.
/// </remarks>
public static class ThreeWayMatch
{
    public static MatchResult Evaluate(Invoice invoice, PurchaseOrder? order, OrderPosition position)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(position);

        if (order is { IsCancelled: true })
        {
            return Decided(MatchOutcome.Rejected, MatchReason.OrderCancelled, "The purchase order was cancelled.");
        }

        if (order is not { IsIssued: true })
        {
            return Decided(
                MatchOutcome.AwaitingPurchaseOrder,
                MatchReason.OrderUnknown,
                "The purchase order has not arrived yet.");
        }

        if (order.SupplierId != invoice.SupplierId)
        {
            return Decided(
                MatchOutcome.Rejected,
                MatchReason.SupplierMismatch,
                $"Purchase order {order.Number} is with another supplier.");
        }

        int[] missing = [.. invoice.Lines.Where(line => order.Line(line.LineNumber) is null).Select(line => line.LineNumber)];
        if (missing.Length > 0)
        {
            return Decided(
                MatchOutcome.Rejected,
                MatchReason.LineNotOnOrder,
                $"Purchase order {order.Number} has no line {string.Join(", ", missing)}.");
        }

        LineFinding[] findings = [.. invoice.Lines.Select(line => Measure(line, order.Line(line.LineNumber)!, position))];

        LineFinding[] shortOfGoods = [.. findings.Where(finding => !finding.QuantityWithinReceived)];
        if (shortOfGoods.Length > 0)
        {
            return new MatchResult(
                MatchOutcome.AwaitingReceipt,
                MatchReason.QuantityExceedsReceived,
                Describe(shortOfGoods, QuantityShortfall),
                findings);
        }

        LineFinding[] offPrice = [.. findings.Where(finding => !finding.PriceWithinTolerance)];
        if (offPrice.Length > 0)
        {
            return invoice.IsVarianceAccepted
                ? new MatchResult(
                    MatchOutcome.Matched,
                    MatchReason.PriceVarianceAccepted,
                    $"Matched with an accepted price variance. {Describe(offPrice, PriceOverrun)}",
                    findings)
                : new MatchResult(
                    MatchOutcome.PriceVariance,
                    MatchReason.PriceVarianceBeyondTolerance,
                    Describe(offPrice, PriceOverrun),
                    findings);
        }

        return new MatchResult(
            MatchOutcome.Matched,
            MatchReason.None,
            "Every line is within what was received and within price tolerance.",
            findings);
    }

    private static LineFinding Measure(InvoiceLine line, OrderedLine ordered, OrderPosition position) =>
        new(
            line.LineNumber,
            line.Quantity,
            position.InvoicedOn(line.LineNumber),
            position.ReceivedOn(line.LineNumber),
            ordered.UnitPrice,
            line.UnitPrice,
            PriceTolerance.Variance(line.Quantity, line.UnitPrice, ordered.UnitPrice),
            PriceTolerance.Allowed(line.Quantity, ordered.UnitPrice));

    private static MatchResult Decided(MatchOutcome outcome, MatchReason reason, string detail) =>
        new(outcome, reason, detail, []);

    private static string Describe(IEnumerable<LineFinding> findings, Func<LineFinding, string> sentence) =>
        string.Join(" ", findings.Select(sentence));

    private static string QuantityShortfall(LineFinding finding) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Line {finding.LineNumber}: {finding.InvoicedBefore + finding.Quantity:0.###} invoiced in total against {finding.Received:0.###} received.");

    private static string PriceOverrun(LineFinding finding) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Line {finding.LineNumber}: unit price {finding.InvoicedUnitPrice:0.00##} against {finding.OrderedUnitPrice:0.00##} ordered, a variance of {finding.Variance:0.00##} where {finding.AllowedVariance:0.00##} is allowed.");
}
