using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

public sealed record RecordReceipt(Guid PurchaseOrderId, IReadOnlyList<LineQuantity> Lines);

/// <param name="ReceiptId">The id <c>GoodsReceived</c> carries, so a client can find the receipt later.</param>
public sealed record RecordedReceipt(Guid ReceiptId, PurchaseOrderView Order);

public sealed class RecordReceiptHandler(IPurchasingDb db, IEventPublisher publisher, TimeProvider clock)
{
    public async Task<RecordedReceipt> HandleAsync(
        RecordReceipt command, Actor receiver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        var receipt = order.RecordReceipt(receiver, command.Lines, clock.GetUtcNow());
        db.GoodsReceipts.Add(receipt);

        await publisher.PublishAsync(OutgoingEvents.GoodsReceived(receipt), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new RecordedReceipt(receipt.Id, PurchaseOrderView.From(order));
    }
}
