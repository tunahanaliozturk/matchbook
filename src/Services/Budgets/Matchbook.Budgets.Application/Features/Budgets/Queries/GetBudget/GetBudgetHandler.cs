using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.GetBudget;

public sealed class GetBudgetHandler(IBudgetsDb db) : IQueryHandler<GetBudgetQuery, BudgetView>
{
    public async Task<BudgetView> HandleAsync(GetBudgetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return BudgetView.From(await db.FindBudgetAsync(query.BudgetId, cancellationToken));
    }
}
