namespace Matchbook.Budgets.Application.Features.Budgets;

/// <summary>An allotment change as recorded, and the budget's balance right after it.</summary>
public sealed record AllotmentChangeView(Guid Id, Guid BudgetId, decimal Change, BudgetView Budget);
