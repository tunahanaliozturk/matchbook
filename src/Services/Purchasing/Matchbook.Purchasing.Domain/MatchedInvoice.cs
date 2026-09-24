namespace Matchbook.Purchasing.Domain;

/// <summary>
/// An invoice whose quantities were already counted against an order. Its id is the natural key that makes a
/// redelivered <c>InvoiceMatched</c> a no-op: the second one finds this row and stops.
/// </summary>
public sealed class MatchedInvoice(Guid invoiceId, Guid purchaseOrderId, DateTimeOffset matchedAt)
{
    public Guid InvoiceId { get; private set; } = invoiceId;

    public Guid PurchaseOrderId { get; private set; } = purchaseOrderId;

    public DateTimeOffset MatchedAt { get; private set; } = matchedAt;
}
