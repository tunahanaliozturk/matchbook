using Matchbook.Contracts.Requisitions;

namespace Matchbook.Budgets.Application.Funds;

/// <summary>An approver rejected the requisition: its reservation is released. Never refused.</summary>
public sealed class RequisitionRejectedHandler(IBudgetsDb db, ReservationReleaser releaser, TimeProvider clock)
{
    public Task HandleAsync(RequisitionRejected message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return db.InTransactionAsync(
            async () =>
            {
                await releaser.ReleaseAsync(message.RequisitionId, message.OccurredAt, clock.GetUtcNow(), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);
    }
}

/// <summary>The requester withdrew the requisition: its reservation is released. Never refused.</summary>
public sealed class RequisitionCancelledHandler(IBudgetsDb db, ReservationReleaser releaser, TimeProvider clock)
{
    public Task HandleAsync(RequisitionCancelled message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return db.InTransactionAsync(
            async () =>
            {
                await releaser.ReleaseAsync(message.RequisitionId, message.OccurredAt, clock.GetUtcNow(), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);
    }
}
