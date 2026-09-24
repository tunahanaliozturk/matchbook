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

    internal static GoodsReceipt Record(
        Guid purchaseOrderId, Guid receivedBy, IEnumerable<LineQuantity> quantities, DateTimeOffset now)
    {
        GoodsReceipt receipt = new(Guid.CreateVersion7(now), purchaseOrderId, receivedBy, now);
        receipt._lines.AddRange(quantities.Select(static q => new GoodsReceiptLine(q.LineNumber, q.Quantity)));
        return receipt;
    }
}
