using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.RejectRequisition;

public sealed class RejectRequisitionHandler(
    IRequisitionsDb db,
    IEventPublisher publisher,
    RequisitionMetrics metrics,
    TimeProvider time) : ICommandHandler<RejectRequisitionCommand, RequisitionView>
{
    public async Task<RequisitionView> HandleAsync(RejectRequisitionCommand command, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(command.RequisitionId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        requisition.Reject(command.Actor, command.Reason, now);

        await publisher.PublishAsync(
            new RequisitionRejected(requisition.Id, command.Actor.Id, requisition.RejectionReason ?? command.Reason, now),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.Rejected();
        return RequisitionView.From(requisition);
    }
}
