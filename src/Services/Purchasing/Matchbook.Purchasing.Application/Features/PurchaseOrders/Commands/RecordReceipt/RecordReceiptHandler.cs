using Matchbook.Purchasing.Application.Common;
using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.RecordReceipt;

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
    : ICommandHandler<RecordReceiptCommand, GoodsReceiptView>
{
    public async Task<GoodsReceiptView> HandleAsync(RecordReceiptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Receiver);

        if (command.ReceiptId is { } id && await StoredAsync(id, cancellationToken) is { } existing)
        {
            return existing.IsRepeatedBy(command.PurchaseOrderId, command.Receiver.Id, command.Lines)
                ? GoodsReceiptView.From(existing)
                : throw new BusinessRuleException(
                    "request.id_reused", $"Receipt id {id} was already used for a different receipt.", ViolationKind.Conflict);
        }

        DateTimeOffset now = clock.GetUtcNow();
        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        var receipt = order.RecordReceipt(command.Receiver, command.ReceiptId ?? Guid.CreateVersion7(now), command.Lines, now);
        db.GoodsReceipts.Add(receipt);

        await publisher.PublishAsync(OutgoingEvents.GoodsReceived(receipt), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.ReceiptRecorded();

        // Answered from the stored row, not the object in memory, because a retry is answered from the row too
        // and has to get the same bytes. The object holds what the request sent (2, a time to 100 ns); the row
        // holds what the columns keep (2.000, a time to the microsecond).
        return GoodsReceiptView.From((await StoredAsync(receipt.Id, cancellationToken))!);
    }

    private Task<GoodsReceipt?> StoredAsync(Guid receiptId, CancellationToken cancellationToken) =>
        db.GoodsReceipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.Id == receiptId, cancellationToken);
}
