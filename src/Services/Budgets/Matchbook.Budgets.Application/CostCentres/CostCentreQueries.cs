using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.CostCentres;

public sealed class GetCostCentreHandler(IBudgetsDb db)
{
    public async Task<CostCentreView> HandleAsync(string code, CancellationToken cancellationToken)
    {
        CostCentre costCentre = await db.CostCentres.AsNoTracking().SingleOrDefaultAsync(c => c.Code == code, cancellationToken)
            ?? throw NotFound.ForCostCentre(code);
        return CostCentreView.From(costCentre);
    }
}

/// <summary>Cost centres in code order. <paramref name="After"/> is the last code of the previous page.</summary>
public sealed record ListCostCentres(string? After = null, int? Limit = null);

public sealed class ListCostCentresHandler(IBudgetsDb db)
{
    public async Task<Page<CostCentreView>> HandleAsync(ListCostCentres query, CancellationToken cancellationToken)
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
