using Matchbook.Budgets.Application.Common;
using Matchbook.Contracts.Requisitions;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.IntegrationEvents;

/// <summary>An approver rejected the requisition: its reservation is released. Never refused.</summary>
public sealed class RequisitionRejectedHandler(IBudgetsDb db, ReservationReleaser releaser, TimeProvider clock)
    : IIntegrationEventHandler<RequisitionRejected>
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
