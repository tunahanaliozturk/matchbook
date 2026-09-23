namespace Matchbook.Contracts.Payables;

/// <summary>
/// A supplier invoice passed the three-way match, or had its variance accepted by an approver, and is now a
/// liability. Budgets moves <see cref="Amount"/> from committed to actual; Purchasing counts the quantities as
/// invoiced.
/// </summary>
public sealed record InvoiceMatched(
    Guid InvoiceId,
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierInvoiceNumber,
    IReadOnlyList<MatchedLine> Lines,
    decimal Amount,
    DateTimeOffset OccurredAt);

/// <summary>An invoiced line as matched. <see cref="Amount"/> is <c>Amounts.Line(Quantity, UnitPrice)</c>.</summary>
public sealed record MatchedLine(int LineNumber, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <summary>A released payment run paid the invoice.</summary>
public sealed record InvoicePaid(
    Guid InvoiceId,
    Guid PurchaseOrderId,
    Guid SupplierId,
    Guid PaymentRunId,
    decimal Amount,
    DateOnly ExecutionDate,
    DateTimeOffset OccurredAt);
