using Matchbook.Requisitions.Application.Common;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListCostCentreOptions;

/// <summary>
/// The cost centres the caller could submit a requisition against, from this service's copy: active, and not one
/// the caller manages, since submitting refuses both. A requester cannot read Budgets, so the form asks here
/// (ADR 0009). Code order, as many as a list returns at most.
/// </summary>
public sealed class ListCostCentreOptionsHandler(IRequisitionsDb db)
    : IQueryHandler<ListCostCentreOptionsQuery, IReadOnlyList<CostCentreOption>>
{
    public async Task<IReadOnlyList<CostCentreOption>> HandleAsync(ListCostCentreOptionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid me = query.Actor.Id;

        // ponytail: capped rather than paged, because a select shows them all; a search parameter when a company
        // has more cost centres than a person would scroll through.
        return await db.CostCentres
            .AsNoTracking()
            .Where(costCentre => costCentre.IsActive && costCentre.ManagerId != me)
            .OrderBy(static costCentre => costCentre.Code)
            .Take(Page.MaxLimit)
            .Select(static costCentre => new CostCentreOption(costCentre.Code, costCentre.Name))
            .ToListAsync(cancellationToken);
    }
}
