namespace Matchbook.Purchasing.Domain;

/// <summary>
/// One delivery recorded against an order: who received it, when, and how much of each line. The order's
/// running totals are what rules are decided on; the receipt is the record of how they got there.
/// </summary>
public sealed class GoodsReceipt
{
    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt(Guid id, Guid purchaseOrderId, Guid receivedBy, DateTimeOffset receivedAt)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        ReceivedBy = receivedBy;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    /// <summary>
    /// True when a request to record a receipt under this id asks for exactly what this one recorded: the same
    /// order, the same person and the same quantity per line. That makes it a retry, answered with this receipt;
    /// anything else reuses the id for a different receipt.
    /// </summary>
    public bool IsRepeatedBy(Guid purchaseOrderId, Guid receivedBy, IEnumerable<LineQuantity> quantities)
    {
        ArgumentNullException.ThrowIfNull(quantities);

        return purchaseOrderId == PurchaseOrderId
            && receivedBy == ReceivedBy
            && quantities
                .GroupBy(static q => q.LineNumber)
                .Select(static line => (line.Key, line.Sum(static q => q.Quantity)))
                .OrderBy(static line => line.Key)
                .SequenceEqual(_lines.Select(static line => (line.LineNumber, line.Quantity)).OrderBy(static line => line.LineNumber));
    }

    internal static GoodsReceipt Record(
        Guid id, Guid purchaseOrderId, Guid receivedBy, IEnumerable<LineQuantity> quantities, DateTimeOffset now)
    {
        GoodsReceipt receipt = new(id, purchaseOrderId, receivedBy, now);
        receipt._lines.AddRange(quantities.Select(static q => new GoodsReceiptLine(q.LineNumber, q.Quantity)));
        return receipt;
    }
}
