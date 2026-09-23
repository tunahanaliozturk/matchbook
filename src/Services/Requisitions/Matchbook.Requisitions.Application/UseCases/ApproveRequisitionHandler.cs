using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class ApproveRequisitionHandler(IRequisitionsDb db, IEventPublisher publisher, TimeProvider time)
{
    public async Task<RequisitionView> HandleAsync(Actor actor, Guid requisitionId, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(requisitionId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        if (requisition.Approve(actor, now))
        {
            await publisher.PublishAsync(OutgoingEvents.Approved(requisition, now), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
