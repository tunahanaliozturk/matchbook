using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.SubmitRequisition;

public sealed class SubmitRequisitionHandler(
    IRequisitionsDb db,
    IEventPublisher publisher,
    RequisitionMetrics metrics,
    TimeProvider time) : ICommandHandler<SubmitRequisitionCommand, RequisitionView>
{
    public async Task<RequisitionView> HandleAsync(SubmitRequisitionCommand command, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(command.RequisitionId, cancellationToken);
        CostCentre? costCentre = await db.CostCentres
            .AsNoTracking()
            .FirstOrDefaultAsync(centre => centre.Code == requisition.CostCentreCode, cancellationToken);
        Supplier? supplier = await db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(known => known.Id == requisition.SupplierId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        requisition.Submit(command.Actor, costCentre, supplier, now);

        await publisher.PublishAsync(OutgoingEvents.Submitted(requisition, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.Submitted();
        return RequisitionView.From(requisition);
    }
}
