using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.CancelRequisition;

public sealed class CancelRequisitionHandler(IRequisitionsDb db, IEventPublisher publisher, TimeProvider time)
    : ICommandHandler<CancelRequisitionCommand, RequisitionView>
{
    public async Task<RequisitionView> HandleAsync(CancelRequisitionCommand command, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(command.RequisitionId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        bool mightHoldReservation = requisition.Cancel(command.Actor, now);

        // A draft never reached Budgets, so there is nothing to release and nobody to tell. Once submitted,
        // Budgets is told even if its answer has not arrived: it leaves a tombstone and refuses a late
        // reservation.
        if (mightHoldReservation)
        {
            await publisher.PublishAsync(new RequisitionCancelled(requisition.Id, command.Actor.Id, now), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
