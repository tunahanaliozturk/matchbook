namespace Matchbook.Payables.Domain.Orders;

/// <summary>
/// A goods receipt as Purchasing recorded it, keyed by its own id so a redelivery is recognised. Stored whether or
/// not the order is known yet.
/// </summary>
public sealed class Receipt
{
    private readonly List<ReceivedLine> _lines = [];

    private Receipt(Guid id, Guid purchaseOrderId, DateTimeOffset receivedAt)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public IReadOnlyList<ReceivedLine> Lines => _lines;

    /// <summary>
    /// Records a receipt. A line number listed twice is summed rather than refused: the goods arrived either way,
    /// and a consumer that throws on it would park the receipt for good.
    /// </summary>
    public static Receipt Record(Guid id, Guid purchaseOrderId, IEnumerable<ReceivedLine> lines, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var receipt = new Receipt(id, purchaseOrderId, receivedAt);
        receipt._lines.AddRange(
            lines.GroupBy(line => line.LineNumber)
                .OrderBy(group => group.Key)
                .Select(group => new ReceivedLine(group.Key, group.Sum(line => line.Quantity))));
        return receipt;
    }
}

/// <summary>The quantity received on one order line in one receipt.</summary>
public sealed record ReceivedLine(int LineNumber, decimal Quantity);
