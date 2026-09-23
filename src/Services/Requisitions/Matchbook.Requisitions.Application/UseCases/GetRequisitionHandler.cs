using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class GetRequisitionHandler(IRequisitionsDb db)
{
    public async Task<RequisitionView> HandleAsync(Actor actor, Guid requisitionId, CancellationToken cancellationToken)
    {
        Requisition? requisition = await db.Requisitions
            .AsNoTracking()
            .Whole()
            .FirstOrDefaultAsync(held => held.Id == requisitionId, cancellationToken);

        // Someone who may not see it gets the same answer as for one that does not exist, so ids cannot be
        // probed for.
        return requisition is not null && requisition.IsVisibleTo(actor)
            ? RequisitionView.From(requisition)
            : throw RequisitionLoading.NotFound(requisitionId);
    }
}
