using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.UnitTests;

public sealed class BudgetTests
{
    [Fact]
    public void Opening_a_budget_records_the_allotment_as_its_first_ledger_entry()
    {
        (Budget budget, LedgerEntry opening) =
            Budget.Open(Some.BudgetId, Some.CostCentre(), 2026, 50_000m, Some.BudgetAdmin, Some.Now);

        budget.Allotted.ShouldBe(50_000m);
        budget.Available.ShouldBe(50_000m);
        opening.BudgetId.ShouldBe(budget.Id);
        opening.DocumentId.ShouldBe(budget.Id);
        opening.Step.ShouldBe(LedgerStep.Open);
        opening.Movement.ShouldBe(Movement.Allot(50_000m));
        opening.ActorId.ShouldBe(Some.BudgetAdmin);
    }

    [Fact]
    public void An_inactive_cost_centre_cannot_be_given_a_budget()
    {
        CostCentre costCentre = Some.CostCentre();
        costCentre.Change(1, costCentre.Name, costCentre.ManagerId, isActive: false);

        new Func<object>(() => Budget.Open(Some.BudgetId, costCentre, 2026, 100m, Some.BudgetAdmin, Some.Now))
            .ShouldBreak("cost_centre.inactive", ViolationKind.Conflict);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Fiscal_years_outside_the_supported_range_are_refused(int fiscalYear) =>
        new Func<object>(() => Budget.Open(Some.BudgetId, Some.CostCentre(), fiscalYear, 100m, Some.BudgetAdmin, Some.Now))
            .ShouldBreak("budget.fiscal_year_invalid", ViolationKind.Invalid);

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100.001")]
    [InlineData("10000000000000000")]
    public void An_allotment_that_does_not_fit_the_column_is_refused(string allotted) =>
        new Func<object>(() => Budget.Open(Some.BudgetId, Some.CostCentre(), 2026, decimal.Parse(allotted, System.Globalization.CultureInfo.InvariantCulture), Some.BudgetAdmin, Some.Now))
            .ShouldBreak("budget.amount_invalid", ViolationKind.Invalid);

    [Fact]
    public void Available_is_the_allotment_less_what_is_reserved_committed_and_spent()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, reserved: 100m, committed: 250m, actual: 400m);

        budget.Consumed.ShouldBe(750m);
        budget.Available.ShouldBe(250m);
        budget.Overspend.ShouldBe(0m);
    }

    [Fact]
    public void A_grant_that_leaves_exactly_nothing_is_affordable_and_one_cent_more_is_not()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, reserved: 600m);

        budget.CanAfford(Movement.Reserve(400m)).ShouldBeTrue();
        budget.CanAfford(Movement.Reserve(400.01m)).ShouldBeFalse();
    }

    [Fact]
    public void A_commitment_is_affordable_when_the_order_fits_in_available_plus_its_reservation()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, reserved: 900m);

        budget.CanAfford(Movement.Commit(reservationHeld: 900m, orderAmount: 1_000m)).ShouldBeTrue();
        budget.CanAfford(Movement.Commit(reservationHeld: 900m, orderAmount: 1_000.01m)).ShouldBeFalse();
    }

    [Fact]
    public void Nothing_can_be_granted_on_an_overspent_budget()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, actual: 1_050m);

        budget.Overspend.ShouldBe(50m);
        budget.CanAfford(Movement.Reserve(0.01m)).ShouldBeFalse();
    }

    [Fact]
    public void Raising_the_allotment_is_allowed_even_on_an_overspent_budget()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, actual: 1_050m);

        budget.ChangeAllotment(10m).ShouldBe(Movement.Allot(10m));
    }

    [Fact]
    public void The_allotment_can_be_lowered_to_exactly_what_is_consumed_and_no_further()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, reserved: 100m, committed: 200m, actual: 300m);

        budget.ChangeAllotment(-400m).ShouldBe(Movement.Allot(-400m));
        new Func<Movement>(() => budget.ChangeAllotment(-400.01m))
            .ShouldBreak("budget.allotment_below_consumed", ViolationKind.Conflict);
    }

    [Fact]
    public void An_allotment_change_of_zero_is_refused() =>
        new Func<Movement>(() => Some.Budget(1_000m).ChangeAllotment(0m))
            .ShouldBreak("budget.allotment_change_zero", ViolationKind.Invalid);

    [Fact]
    public void Applying_a_movement_that_would_make_a_figure_negative_is_a_bug_not_a_rule()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, reserved: 100m);

        Should.Throw<InvalidOperationException>(() => budget.Apply(Movement.ReleaseReservation(100.01m)));
        budget.Reserved.ShouldBe(100m);
    }

    [Fact]
    public void Applying_a_movement_that_would_reserve_and_commit_more_than_the_allotment_is_refused()
    {
        Budget budget = Some.BudgetWith(allotted: 1_000m, reserved: 1_000m);

        Should.Throw<InvalidOperationException>(() => budget.Apply(Movement.Commit(0m, 0.01m)));
    }
}
