using Matchbook.Budgets.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.ListBudgets;

/// <summary>One fiscal year's budgets. <paramref name="After"/> is the last cost centre code of the previous page.</summary>
public sealed record ListBudgetsQuery(int FiscalYear, string? After = null, int? Limit = null) : IQuery<Page<BudgetView>>;
