using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>
/// Sends a draft to Budgets for commitment. The order is not issued yet: it waits in
/// <c>CommitmentPending</c> until <c>FundsCommitted</c> or <c>FundsCommitmentRejected</c> comes back.
/// </summary>
public sealed class IssuePurchaseOrderHandler(IPurchasingDb db, IEventPublisher publisher, TimeProvider clock)
{
    public async Task<PurchaseOrderView> HandleAsync(
        Guid purchaseOrderId, Actor buyer, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.GetForChangeAsync(purchaseOrderId, cancellationToken);
        var supplier = await db.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(supplier => supplier.Id == order.SupplierId, cancellationToken);

        order.RequestIssue(buyer, supplier);

        await publisher.PublishAsync(OutgoingEvents.CommitmentRequested(order, clock.GetUtcNow()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return PurchaseOrderView.From(order);
    }
}
