using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

public sealed class GetPurchaseOrderHandler(IPurchasingDb db)
{
    public async Task<PurchaseOrderView> HandleAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken)
            ?? throw PurchaseOrderLookup.NotFound(purchaseOrderId);

        return PurchaseOrderView.From(order);
    }
}
