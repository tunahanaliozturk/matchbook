using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Features.CostCentres.Queries.GetCostCentre;

public sealed class GetCostCentreHandler(IBudgetsDb db) : IQueryHandler<GetCostCentreQuery, CostCentreView>
{
    public async Task<CostCentreView> HandleAsync(GetCostCentreQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CostCentre costCentre = await db.CostCentres.AsNoTracking().SingleOrDefaultAsync(c => c.Code == query.Code, cancellationToken)
            ?? throw NotFound.ForCostCentre(query.Code);
        return CostCentreView.From(costCentre);
    }
}
