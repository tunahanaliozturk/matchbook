using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.GetBudget;

public sealed record GetBudgetQuery(Guid BudgetId) : IQuery<BudgetView>;
