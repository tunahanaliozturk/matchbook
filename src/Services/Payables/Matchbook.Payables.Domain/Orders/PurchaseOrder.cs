namespace Matchbook.Payables.Domain.Orders;

/// <summary>
/// What Payables knows about a purchase order, built from facts that arrive in any order: that it was issued, with
/// its lines, and that it was closed. The row exists from the first time anything mentions the order, an invoice
/// or a receipt included, before Purchasing's own event has arrived.
/// </summary>
/// <remarks>
/// It is also the point every match on the order serialises on. Whatever can change the outcome of a match (a new
/// fact about the order, a receipt, an invoice evaluated against it) calls <see cref="Revise"/> in the same unit of
/// work, and <see cref="Revision"/> is the row's concurrency token. Two such changes that start from the same
/// revision cannot both commit, so a receipt and an invoice arriving together cannot each miss the other, and two
/// invoices cannot both claim the last received quantity.
/// </remarks>
public sealed class PurchaseOrder
{
    private readonly List<OrderedLine> _lines = [];

    private PurchaseOrder(Guid id) => Id = id;

    public Guid Id { get; private set; }

    public string? Number { get; private set; }

    public Guid? SupplierId { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public IReadOnlyList<OrderedLine> Lines => _lines;

    /// <summary>The close reason as Purchasing sent it, kept for people to read.</summary>
    public string? CloseReason { get; private set; }

    public bool IsCancelled { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public long Revision { get; private set; }

    public bool IsIssued => IssuedAt is not null;

    public static PurchaseOrder FirstMentioned(Guid id) => new(id);

    /// <summary>Records the issued order. Returns false when it was already known, which is a redelivery.</summary>
    public bool RecordIssue(string number, Guid supplierId, IEnumerable<OrderedLine> lines, DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(lines);

        if (IsIssued)
        {
            return false;
        }

        Number = number;
        SupplierId = supplierId;
        IssuedAt = issuedAt;
        _lines.AddRange(lines);
        return true;
    }

    /// <summary>
    /// Records that the order was closed. Kept even when the order itself has not arrived, so an invoice for an order
    /// cancelled before Payables heard of it is still refused. Returns false for a redelivery.
    /// </summary>
    public bool RecordClosure(string reason, bool cancelled, DateTimeOffset closedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (ClosedAt is not null)
        {
            return false;
        }

        CloseReason = reason;
        IsCancelled = cancelled;
        ClosedAt = closedAt;
        return true;
    }

    public void Revise() => Revision++;

    public OrderedLine? Line(int lineNumber) => _lines.Find(line => line.LineNumber == lineNumber);
}

/// <summary>An order line as issued. Lines and prices are final once the order is issued.</summary>
public sealed record OrderedLine(int LineNumber, decimal Quantity, decimal UnitPrice);
