namespace Matchbook.Budgets.Application.Budgets;

public sealed class GetBudgetHandler(IBudgetsDb db)
{
    public async Task<BudgetView> HandleAsync(Guid budgetId, CancellationToken cancellationToken) =>
        BudgetView.From(await db.FindBudgetAsync(budgetId, cancellationToken));
}

/// <summary>One fiscal year's budgets. <paramref name="After"/> is the last cost centre code of the previous page.</summary>
public sealed record ListBudgets(int FiscalYear, string? After = null, int? Limit = null);

public sealed class ListBudgetsHandler(IBudgetsDb db)
{
    public Task<Page<BudgetView>> HandleAsync(ListBudgets query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return db.Budgets
            .Where(b => b.FiscalYear == query.FiscalYear)
            .PageAsync(query.After, query.Limit, cancellationToken);
    }
}

/// <summary>
/// The budgets of a fiscal year whose consumption has run past the allotment. Only an invoice larger than its
/// commitment can put a budget here, and the report is where that shows up.
/// </summary>
public sealed record OverspendReport(int FiscalYear, string? After = null, int? Limit = null);

public sealed class OverspendReportHandler(IBudgetsDb db)
{
    public Task<Page<BudgetView>> HandleAsync(OverspendReport query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return db.Budgets
            .Where(b => b.FiscalYear == query.FiscalYear && b.Reserved + b.Committed + b.Actual > b.Allotted)
            .PageAsync(query.After, query.Limit, cancellationToken);
    }
}
