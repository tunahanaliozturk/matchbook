using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

public sealed class ShortClosePurchaseOrderHandler(IPurchasingDb db, IEventPublisher publisher, TimeProvider clock)
{
    public async Task<PurchaseOrderView> HandleAsync(
        Guid purchaseOrderId, Actor buyer, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.GetForChangeAsync(purchaseOrderId, cancellationToken);
        order.ShortClose(buyer, clock.GetUtcNow());

        await publisher.PublishAsync(OutgoingEvents.Closed(order), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return PurchaseOrderView.From(order);
    }
}
