using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>
/// The order drafted for a requisition, for a client that follows a requisition to its order. There is at most
/// one, by the unique index on the requisition id; a 404 means the approval has not been consumed yet.
/// </summary>
public sealed class GetPurchaseOrderForRequisitionHandler(IPurchasingDb db)
{
    public async Task<PurchaseOrderView> HandleAsync(Guid requisitionId, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.RequisitionId == requisitionId, cancellationToken)
            ?? throw new BusinessRuleException(
                "purchase_order.not_found",
                $"No purchase order has been drafted for requisition {requisitionId}.",
                ViolationKind.NotFound);

        return PurchaseOrderView.From(order);
    }
}
