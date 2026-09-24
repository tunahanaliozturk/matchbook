using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.ApproveRequisition;

public sealed class ApproveRequisitionHandler(
    IRequisitionsDb db,
    IEventPublisher publisher,
    RequisitionMetrics metrics,
    TimeProvider time) : ICommandHandler<ApproveRequisitionCommand, RequisitionView>
{
    public async Task<RequisitionView> HandleAsync(ApproveRequisitionCommand command, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(command.RequisitionId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        bool last = requisition.Approve(command.Actor, now);
        if (last)
        {
            await publisher.PublishAsync(OutgoingEvents.Approved(requisition, now), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (last)
        {
            metrics.Approved(requisition);
        }

        return RequisitionView.From(requisition);
    }
}
