using Matchbook.Budgets.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.OverspendReport;

public sealed class OverspendReportHandler(IBudgetsDb db) : IQueryHandler<OverspendReportQuery, Page<BudgetView>>
{
    public Task<Page<BudgetView>> HandleAsync(OverspendReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return db.Budgets
            .Where(b => b.FiscalYear == query.FiscalYear && b.Reserved + b.Committed + b.Actual > b.Allotted)
            .PageAsync(query.After, query.Limit, cancellationToken);
    }
}
