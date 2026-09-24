using Matchbook.Purchasing.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.ShortClosePurchaseOrder;

public sealed class ShortClosePurchaseOrderHandler(
    IPurchasingDb db, IEventPublisher publisher, TimeProvider clock, PurchasingMetrics metrics)
    : ICommandHandler<ShortClosePurchaseOrderCommand, PurchaseOrderView>
{
    public async Task<PurchaseOrderView> HandleAsync(ShortClosePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        order.ShortClose(command.Buyer, clock.GetUtcNow());

        await publisher.PublishAsync(OutgoingEvents.Closed(order), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.OrderClosed(order.Status);
        return PurchaseOrderView.From(order);
    }
}
