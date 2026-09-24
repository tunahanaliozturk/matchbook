using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListRequisitions;

/// <summary>
/// The requisitions the caller may read, newest first: their own, or every one for approvers and the auditor,
/// by the same rule that decides who may read a single requisition.
/// </summary>
public sealed class ListRequisitionsHandler(IRequisitionsDb db)
    : IQueryHandler<ListRequisitionsQuery, Page<RequisitionSummary>>
{
    public async Task<Page<RequisitionSummary>> HandleAsync(ListRequisitionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        (long? after, int? limit, Actor actor) = query;
        int take = Page.Clamp(limit);

        IQueryable<Requisition> visible = db.Requisitions.AsNoTracking();
        if (!Requisition.SeesEveryRequisition(actor))
        {
            visible = visible.Where(requisition => requisition.RequesterId == actor.Id);
        }

        if (after is { } cursor)
        {
            visible = visible.Where(requisition => requisition.Serial < cursor);
        }

        List<Keyed<RequisitionSummary>> rows = await visible
            .OrderByDescending(static requisition => requisition.Serial)
            .Take(take + 1)
            .Select(RequisitionSummary.KeyedBySerial)
            .ToListAsync(cancellationToken);

        return Page.Of(rows, take);
    }
}
