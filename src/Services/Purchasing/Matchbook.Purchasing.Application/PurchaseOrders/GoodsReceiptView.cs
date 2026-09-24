using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>A goods receipt as the API returns it. It never changes once recorded.</summary>
public sealed record GoodsReceiptView(
    Guid Id,
    Guid PurchaseOrderId,
    Guid ReceivedBy,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<ReceiptLineView> Lines)
{
    public static GoodsReceiptView From(GoodsReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new(
            receipt.Id,
            receipt.PurchaseOrderId,
            receipt.ReceivedBy,
            receipt.ReceivedAt,
            [.. receipt.Lines.OrderBy(static line => line.LineNumber).Select(static line => new ReceiptLineView(line.LineNumber, line.Quantity))]);
    }
}

public sealed record ReceiptLineView(int LineNumber, decimal Quantity);
