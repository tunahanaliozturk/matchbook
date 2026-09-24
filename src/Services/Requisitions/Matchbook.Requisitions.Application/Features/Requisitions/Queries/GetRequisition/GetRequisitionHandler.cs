using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.GetRequisition;

public sealed class GetRequisitionHandler(IRequisitionsDb db) : IQueryHandler<GetRequisitionQuery, RequisitionView>
{
    public async Task<RequisitionView> HandleAsync(GetRequisitionQuery query, CancellationToken cancellationToken)
    {
        Requisition? requisition = await db.Requisitions
            .AsNoTracking()
            .Whole()
            .FirstOrDefaultAsync(held => held.Id == query.RequisitionId, cancellationToken);

        // Someone who may not see it gets the same answer as for one that does not exist, so ids cannot be
        // probed for.
        return requisition is not null && requisition.IsVisibleTo(query.Actor)
            ? RequisitionView.From(requisition)
            : throw RequisitionLoading.NotFound(query.RequisitionId);
    }
}
