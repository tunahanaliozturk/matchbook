using Matchbook.Budgets.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.ListBudgets;

public sealed class ListBudgetsHandler(IBudgetsDb db) : IQueryHandler<ListBudgetsQuery, Page<BudgetView>>
{
    public Task<Page<BudgetView>> HandleAsync(ListBudgetsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return db.Budgets
            .Where(b => b.FiscalYear == query.FiscalYear)
            .PageAsync(query.After, query.Limit, cancellationToken);
    }
}
