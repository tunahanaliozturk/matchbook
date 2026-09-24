namespace Matchbook.Payables.Domain.Invoices;

/// <summary>What the three-way match decided.</summary>
public enum MatchOutcome
{
    Matched,
    AwaitingPurchaseOrder,
    AwaitingReceipt,
    PriceVariance,
    Rejected,
}

/// <summary>Why the match decided what it did. Stored by name on the invoice.</summary>
public enum MatchReason
{
    /// <summary>Every line is within what was received and within price tolerance.</summary>
    None,

    OrderUnknown,
    OrderCancelled,
    SupplierMismatch,
    LineNotOnOrder,
    QuantityExceedsReceived,
    PriceVarianceBeyondTolerance,

    /// <summary>Matched, with a price variance an approver accepted.</summary>
    PriceVarianceAccepted,
}

/// <summary>
/// The outcome of matching one invoice, with the reason and, once the order is known, the arithmetic behind it
/// for every line.
/// </summary>
public sealed record MatchResult(
    MatchOutcome Outcome,
    MatchReason Reason,
    string Detail,
    IReadOnlyList<LineFinding> Lines);

/// <summary>
/// One invoice line measured against the order and its receipts. <see cref="InvoicedBefore"/> is what other matched
/// invoices already billed on the same order line.
/// </summary>
public sealed record LineFinding(
    int LineNumber,
    decimal Quantity,
    decimal InvoicedBefore,
    decimal Received,
    decimal OrderedUnitPrice,
    decimal InvoicedUnitPrice,
    decimal Variance,
    decimal AllowedVariance)
{
    public bool QuantityWithinReceived => InvoicedBefore + Quantity <= Received;

    public bool PriceWithinTolerance => Variance <= AllowedVariance;
}
