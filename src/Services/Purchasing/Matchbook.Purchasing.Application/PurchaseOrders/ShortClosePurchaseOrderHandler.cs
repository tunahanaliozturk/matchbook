using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

public sealed class ShortClosePurchaseOrderHandler(
    IPurchasingDb db, IEventPublisher publisher, TimeProvider clock, PurchasingMetrics metrics)
{
    public async Task<PurchaseOrderView> HandleAsync(
        Guid purchaseOrderId, Actor buyer, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.GetForChangeAsync(purchaseOrderId, cancellationToken);
        order.ShortClose(buyer, clock.GetUtcNow());

        await publisher.PublishAsync(OutgoingEvents.Closed(order), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.OrderClosed(order.Status);
        return PurchaseOrderView.From(order);
    }
}
