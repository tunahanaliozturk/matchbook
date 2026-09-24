using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class RejectRequisitionHandler(
    IRequisitionsDb db,
    IEventPublisher publisher,
    RequisitionMetrics metrics,
    TimeProvider time)
{
    public async Task<RequisitionView> HandleAsync(
        Actor actor,
        Guid requisitionId,
        string reason,
        CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(requisitionId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        requisition.Reject(actor, reason, now);

        await publisher.PublishAsync(
            new RequisitionRejected(requisition.Id, actor.Id, requisition.RejectionReason ?? reason, now),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.Rejected();
        return RequisitionView.From(requisition);
    }
}
