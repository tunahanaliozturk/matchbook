using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class SubmitRequisitionHandler(IRequisitionsDb db, IEventPublisher publisher, TimeProvider time)
{
    public async Task<RequisitionView> HandleAsync(Actor actor, Guid requisitionId, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(requisitionId, cancellationToken);
        CostCentre? costCentre = await db.CostCentres
            .AsNoTracking()
            .FirstOrDefaultAsync(centre => centre.Code == requisition.CostCentreCode, cancellationToken);
        Supplier? supplier = await db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(known => known.Id == requisition.SupplierId, cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        requisition.Submit(actor, costCentre, supplier, now);

        await publisher.PublishAsync(OutgoingEvents.Submitted(requisition, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
