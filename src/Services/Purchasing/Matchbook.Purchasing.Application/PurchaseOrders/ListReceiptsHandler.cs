using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>Every receipt against an order, oldest first. An order has a handful, so there is no paging.</summary>
public sealed class ListReceiptsHandler(IPurchasingDb db)
{
    public async Task<IReadOnlyList<GoodsReceiptView>> HandleAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var receipts = await db.GoodsReceipts
            .AsNoTracking()
            .Where(receipt => receipt.PurchaseOrderId == purchaseOrderId)
            .OrderBy(receipt => receipt.ReceivedAt)
            .ToListAsync(cancellationToken);

        // Only an empty answer needs the second query, to tell "no receipts yet" from "no such order".
        if (receipts.Count == 0 && !await db.PurchaseOrders.AnyAsync(order => order.Id == purchaseOrderId, cancellationToken))
        {
            throw PurchaseOrderLookup.NotFound(purchaseOrderId);
        }

        return [.. receipts.Select(GoodsReceiptView.From)];
    }
}
