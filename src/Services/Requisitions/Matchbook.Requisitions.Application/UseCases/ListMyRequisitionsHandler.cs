using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.UseCases;

/// <summary>The caller's own requisitions, newest first.</summary>
public sealed class ListMyRequisitionsHandler(IRequisitionsDb db)
{
    public async Task<Page<RequisitionSummary>> HandleAsync(
        Actor actor,
        long? after,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        int take = Page.Clamp(limit);

        IQueryable<Requisition> mine = db.Requisitions.AsNoTracking().Where(requisition => requisition.RequesterId == actor.Id);
        if (after is { } cursor)
        {
            mine = mine.Where(requisition => requisition.Serial < cursor);
        }

        List<Keyed<RequisitionSummary>> rows = await mine
            .OrderByDescending(static requisition => requisition.Serial)
            .Take(take + 1)
            .Select(RequisitionSummary.KeyedBySerial)
            .ToListAsync(cancellationToken);

        return Page.Of(rows, take);
    }
}
