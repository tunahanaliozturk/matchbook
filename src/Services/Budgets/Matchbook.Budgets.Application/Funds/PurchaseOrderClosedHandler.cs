using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Purchasing;

namespace Matchbook.Budgets.Application.Funds;

/// <summary>
/// Releases whatever an order still holds: its remaining commitment, or, if it was never committed, its
/// requisition's reservation. Never refused.
/// </summary>
public sealed class PurchaseOrderClosedHandler(IBudgetsDb db, ReservationReleaser releaser, TimeProvider clock)
{
    public Task HandleAsync(PurchaseOrderClosed message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return db.InTransactionAsync(() => CloseAsync(message, cancellationToken), cancellationToken);
    }

    private async Task CloseAsync(PurchaseOrderClosed message, CancellationToken cancellationToken)
    {
        OrderCommitment? order = await db.Commitments.FindAsync([message.PurchaseOrderId], cancellationToken);
        if (order is null)
        {
            // Closed before any commitment request arrived. The row is the tombstone a late request is refused on.
            order = OrderCommitment.Track(message.PurchaseOrderId, message.RequisitionId);
            db.Commitments.Add(order);
        }
        else if (order.Status == CommitmentStatus.Closed)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (order.Close(message.OccurredAt, now) is { } entry)
        {
            await db.ApplyAsync(entry, cancellationToken);
            db.Ledger.Add(entry);
        }

        if (!order.WasCommitted)
        {
            await releaser.ReleaseAsync(order.RequisitionId, message.OccurredAt, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
