using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class CancelRequisitionHandler(IRequisitionsDb db, IEventPublisher publisher, TimeProvider time)
{
    public async Task<RequisitionView> HandleAsync(Actor actor, Guid requisitionId, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(requisitionId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        bool mightHoldReservation = requisition.Cancel(actor, now);

        // A draft never reached Budgets, so there is nothing to release and nobody to tell. Once submitted,
        // Budgets is told even if its answer has not arrived: it leaves a tombstone and refuses a late
        // reservation.
        if (mightHoldReservation)
        {
            await publisher.PublishAsync(new RequisitionCancelled(requisition.Id, actor.Id, now), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
