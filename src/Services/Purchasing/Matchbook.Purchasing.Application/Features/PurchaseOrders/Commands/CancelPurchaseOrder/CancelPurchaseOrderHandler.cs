using Matchbook.Purchasing.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

/// <summary>
/// Cancels an order and tells Budgets to let go of whatever it holds for it: the commitment if the order was
/// committed, otherwise the requisition's reservation.
/// </summary>
public sealed class CancelPurchaseOrderHandler(
    IPurchasingDb db, IEventPublisher publisher, TimeProvider clock, PurchasingMetrics metrics)
    : ICommandHandler<CancelPurchaseOrderCommand, PurchaseOrderView>
{
    public async Task<PurchaseOrderView> HandleAsync(CancelPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        order.Cancel(command.Buyer, clock.GetUtcNow());

        await publisher.PublishAsync(OutgoingEvents.Closed(order), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.OrderClosed(order.Status);
        return PurchaseOrderView.From(order, await db.Suppliers.NameOfAsync(order.SupplierId, cancellationToken));
    }
}
