using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Features.Budgets;

/// <summary>A budget's balance: the four figures, what is available, and how far it is overspent.</summary>
public sealed record BudgetView(
    Guid Id,
    string CostCentreCode,
    int FiscalYear,
    decimal Allotted,
    decimal Reserved,
    decimal Committed,
    decimal Actual,
    decimal Available,
    decimal Overspend)
{
    internal static BudgetView From(Budget budget) =>
        new(
            budget.Id,
            budget.CostCentreCode,
            budget.FiscalYear,
            budget.Allotted,
            budget.Reserved,
            budget.Committed,
            budget.Actual,
            budget.Available,
            budget.Overspend);
}

internal static class BudgetReads
{
    public static async Task<Budget> FindBudgetAsync(this IBudgetsDb db, Guid budgetId, CancellationToken cancellationToken) =>
        await db.Budgets.AsNoTracking().SingleOrDefaultAsync(b => b.Id == budgetId, cancellationToken)
        ?? throw NotFound.ForBudget(budgetId);

    /// <summary>One fiscal year's budgets in cost centre order, a page at a time.</summary>
    public static async Task<Page<BudgetView>> PageAsync(
        this IQueryable<Budget> budgets, string? after, int? requestedLimit, CancellationToken cancellationToken)
    {
        int limit = Paging.Limit(requestedLimit);
        if (after is not null)
        {
            budgets = budgets.Where(b => b.CostCentreCode.CompareTo(after) > 0);
        }

        List<Budget> rows = await budgets
            .AsNoTracking()
            .OrderBy(b => b.CostCentreCode)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return Paging.Of(rows.ConvertAll(BudgetView.From), limit, view => view.CostCentreCode);
    }
}
