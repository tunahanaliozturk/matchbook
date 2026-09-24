using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>
/// Cancels an order and tells Budgets to let go of whatever it holds for it: the commitment if the order was
/// committed, otherwise the requisition's reservation.
/// </summary>
public sealed class CancelPurchaseOrderHandler(IPurchasingDb db, IEventPublisher publisher, TimeProvider clock)
{
    public async Task<PurchaseOrderView> HandleAsync(
        Guid purchaseOrderId, Actor buyer, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.GetForChangeAsync(purchaseOrderId, cancellationToken);
        order.Cancel(buyer, clock.GetUtcNow());

        await publisher.PublishAsync(OutgoingEvents.Closed(order), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return PurchaseOrderView.From(order);
    }
}
