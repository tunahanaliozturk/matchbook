using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Features.CostCentres.Queries.ListCostCentres;

public sealed class ListCostCentresHandler(IBudgetsDb db) : IQueryHandler<ListCostCentresQuery, Page<CostCentreView>>
{
    public async Task<Page<CostCentreView>> HandleAsync(ListCostCentresQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        int limit = Paging.Limit(query.Limit);
        IQueryable<CostCentre> costCentres = db.CostCentres.AsNoTracking();
        if (query.After is { } after)
        {
            costCentres = costCentres.Where(c => c.Code.CompareTo(after) > 0);
        }

        List<CostCentre> rows = await costCentres.OrderBy(c => c.Code).Take(limit + 1).ToListAsync(cancellationToken);
        return Paging.Of(rows.ConvertAll(CostCentreView.From), limit, view => view.Code);
    }
}
