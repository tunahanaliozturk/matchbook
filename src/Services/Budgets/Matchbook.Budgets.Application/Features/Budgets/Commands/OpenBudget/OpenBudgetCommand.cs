using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Commands.OpenBudget;

/// <param name="Id">Optional. Becomes the budget's id; repeating it returns the first result.</param>
/// <param name="Actor">The budget admin opening it, recorded on the opening ledger entry.</param>
public sealed record OpenBudgetCommand(Guid? Id, string CostCentreCode, int FiscalYear, decimal Allotted, Actor Actor)
    : ICommand<BudgetView>;
