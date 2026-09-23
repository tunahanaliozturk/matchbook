namespace Matchbook.Contracts.Purchasing;

/// <summary>
/// A buyer asked to issue a purchase order. Budgets answers with <c>FundsCommitted</c> or
/// <c>FundsCommitmentRejected</c> carrying the same <see cref="Attempt"/>, which rises by one each time the
/// same order is sent for commitment again.
/// </summary>
public sealed record PurchaseOrderCommitmentRequested(
    Guid PurchaseOrderId,
    Guid RequisitionId,
    int Attempt,
    string CostCentreCode,
    int FiscalYear,
    decimal Amount,
    DateTimeOffset OccurredAt);

/// <summary>The order is committed and sent to the supplier. Its lines and prices are final from here.</summary>
public sealed record PurchaseOrderIssued(
    Guid PurchaseOrderId,
    string Number,
    Guid RequisitionId,
    Guid SupplierId,
    string CostCentreCode,
    int FiscalYear,
    IReadOnlyList<PurchaseOrderLine> Lines,
    decimal Amount,
    Guid IssuedBy,
    DateTimeOffset OccurredAt);

/// <summary>A purchase order line. <see cref="Amount"/> is <c>Amounts.Line(Quantity, UnitPrice)</c>.</summary>
public sealed record PurchaseOrderLine(
    int LineNumber,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal Amount);

/// <summary>Goods arrived against an issued order. Quantities are this receipt's, not running totals.</summary>
public sealed record GoodsReceived(
    Guid ReceiptId,
    Guid PurchaseOrderId,
    IReadOnlyList<ReceiptLine> Lines,
    Guid ReceivedBy,
    DateTimeOffset OccurredAt);

/// <summary>The quantity received on one line in one receipt.</summary>
public sealed record ReceiptLine(int LineNumber, decimal Quantity);

/// <summary>
/// The order is finished with. Budgets releases whatever it still holds for it: the remaining commitment, or
/// the reservation for its requisition if the order was never committed. <see cref="Reason"/> is one of
/// <see cref="PurchaseOrderCloseReason"/>.
/// </summary>
public sealed record PurchaseOrderClosed(
    Guid PurchaseOrderId,
    Guid RequisitionId,
    string Reason,
    DateTimeOffset OccurredAt);

/// <summary>Values of <see cref="PurchaseOrderClosed.Reason"/>.</summary>
public static class PurchaseOrderCloseReason
{
    /// <summary>Everything ordered was received and invoiced.</summary>
    public const string Completed = "Completed";

    /// <summary>The buyer closed it with quantities still open. Nothing more will be received.</summary>
    public const string ShortClosed = "ShortClosed";

    /// <summary>Cancelled before anything was received.</summary>
    public const string Cancelled = "Cancelled";
}
