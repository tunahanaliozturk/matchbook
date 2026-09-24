using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application;

internal static class NotFound
{
    public static BusinessRuleException ForCostCentre(string code) =>
        new("cost_centre.not_found", $"There is no cost centre {code}.", ViolationKind.NotFound);

    public static BusinessRuleException ForBudget(Guid budgetId) =>
        new("budget.not_found", $"There is no budget {budgetId}.", ViolationKind.NotFound);
}
