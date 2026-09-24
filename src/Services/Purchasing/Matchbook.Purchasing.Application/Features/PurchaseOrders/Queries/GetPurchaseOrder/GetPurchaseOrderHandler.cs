using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetPurchaseOrder;

public sealed class GetPurchaseOrderHandler(IPurchasingDb db) : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderView>
{
    public async Task<PurchaseOrderView> HandleAsync(GetPurchaseOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await db.PurchaseOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == query.PurchaseOrderId, cancellationToken)
            ?? throw PurchaseOrderLookup.NotFound(query.PurchaseOrderId);

        return PurchaseOrderView.From(order);
    }
}
