using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.UnitTests;

/// <summary>Values the tests share, fixed so a failure reads the same every run.</summary>
internal static class Some
{
    public static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 30, 0, TimeSpan.Zero);
    public static readonly Guid Manager = new("a0000000-0000-4000-8000-000000000002");
    public static readonly Guid BudgetAdmin = new("a0000000-0000-4000-8000-000000000014");
    public static readonly Guid BudgetId = new("b0000000-0000-4000-8000-000000000001");

    public static CostCentre CostCentre(string code = "ENG-PLATFORM") =>
        Domain.CostCentre.Create(code, "Platform engineering", Manager);

    public static Budget Budget(decimal allotted) =>
        Domain.Budget.Open(BudgetId, CostCentre(), 2026, allotted, BudgetAdmin, Now).Budget;

    public static Budget BudgetWith(decimal allotted, decimal reserved = 0m, decimal committed = 0m, decimal actual = 0m)
    {
        Budget budget = Budget(allotted);
        budget.Apply(new Movement(0m, reserved, committed, 0m));
        budget.Apply(new Movement(0m, 0m, 0m, actual));
        return budget;
    }

    public static BusinessRuleException ShouldBreak(this Action action, string code, ViolationKind kind)
    {
        BusinessRuleException broken = Should.Throw<BusinessRuleException>(action);
        broken.Code.ShouldBe(code);
        broken.Kind.ShouldBe(kind);
        return broken;
    }

    public static BusinessRuleException ShouldBreak<T>(this Func<T> action, string code, ViolationKind kind) =>
        new Action(() => action()).ShouldBreak(code, kind);
}
