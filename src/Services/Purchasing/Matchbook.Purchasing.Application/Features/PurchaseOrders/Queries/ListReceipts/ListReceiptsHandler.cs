using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListReceipts;

/// <summary>Every receipt against an order, oldest first. An order has a handful, so there is no paging.</summary>
public sealed class ListReceiptsHandler(IPurchasingDb db) : IQueryHandler<ListReceiptsQuery, IReadOnlyList<GoodsReceiptView>>
{
    public async Task<IReadOnlyList<GoodsReceiptView>> HandleAsync(ListReceiptsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var receipts = await db.GoodsReceipts
            .AsNoTracking()
            .Where(receipt => receipt.PurchaseOrderId == query.PurchaseOrderId)
            .OrderBy(receipt => receipt.ReceivedAt)
            .ToListAsync(cancellationToken);

        // Only an empty answer needs the second query, to tell "no receipts yet" from "no such order".
        if (receipts.Count == 0 && !await db.PurchaseOrders.AnyAsync(order => order.Id == query.PurchaseOrderId, cancellationToken))
        {
            throw PurchaseOrderLookup.NotFound(query.PurchaseOrderId);
        }

        return [.. receipts.Select(GoodsReceiptView.From)];
    }
}
