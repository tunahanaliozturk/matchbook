using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <param name="ReceiptId">
/// The client's id for the receipt, so a retry after a lost response is recognised. Null lets the server pick one.
/// </param>
public sealed record RecordReceipt(Guid PurchaseOrderId, Guid? ReceiptId, IReadOnlyList<LineQuantity> Lines);

/// <summary>
/// Records a delivery and publishes <c>GoodsReceived</c> with its quantities. Idempotent on the receipt id: the same
/// request again returns the receipt the first one recorded, and the same id for anything else is refused.
/// </summary>
/// <remarks>
/// The replay check runs before any rule, so a retry still gets its receipt after the order has moved on, say
/// to short-closed. Two identical requests racing each other both miss the check; the second then fails on the
/// order's row version or the receipt's primary key, both answered as <c>concurrency.conflict</c>, and its retry
/// finds the receipt.
/// </remarks>
public sealed class RecordReceiptHandler(
    IPurchasingDb db, IEventPublisher publisher, TimeProvider clock, PurchasingMetrics metrics)
{
    public async Task<GoodsReceiptView> HandleAsync(
        RecordReceipt command, Actor receiver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(receiver);

        if (command.ReceiptId is { } id
            && await db.GoodsReceipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.Id == id, cancellationToken) is { } existing)
        {
            return existing.IsRepeatedBy(command.PurchaseOrderId, receiver.Id, command.Lines)
                ? GoodsReceiptView.From(existing)
                : throw new BusinessRuleException(
                    "request.id_reused", $"Receipt id {id} was already used for a different receipt.", ViolationKind.Conflict);
        }

        DateTimeOffset now = clock.GetUtcNow();
        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        var receipt = order.RecordReceipt(receiver, command.ReceiptId ?? Guid.CreateVersion7(now), command.Lines, now);
        db.GoodsReceipts.Add(receipt);

        await publisher.PublishAsync(OutgoingEvents.GoodsReceived(receipt), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        metrics.ReceiptRecorded();
        return GoodsReceiptView.From(receipt);
    }
}
