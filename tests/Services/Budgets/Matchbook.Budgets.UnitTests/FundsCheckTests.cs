using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.UnitTests;

public sealed class FundsCheckTests
{
    private static readonly Guid Requisition = Guid.CreateVersion7();

    [Fact]
    public void A_reservation_against_an_unknown_cost_centre_is_refused_as_unavailable()
    {
        FundsCheck.RefusesReservation(costCentre: null, Some.Budget(100m), out FundsRefusal refusal).ShouldBeTrue();
        refusal.ShouldBe(FundsRefusal.CostCentreUnavailable);
    }

    [Fact]
    public void A_reservation_against_an_inactive_cost_centre_is_refused_as_unavailable()
    {
        CostCentre costCentre = Some.CostCentre();
        Budget budget = Some.Budget(100m);
        costCentre.Change(1, costCentre.Name, costCentre.ManagerId, isActive: false);

        FundsCheck.RefusesReservation(costCentre, budget, out FundsRefusal refusal).ShouldBeTrue();
        refusal.ShouldBe(FundsRefusal.CostCentreUnavailable);
    }

    [Fact]
    public void A_reservation_for_a_year_without_a_budget_is_refused_as_no_budget()
    {
        FundsCheck.RefusesReservation(Some.CostCentre(), budget: null, out FundsRefusal refusal).ShouldBeTrue();
        refusal.ShouldBe(FundsRefusal.NoBudget);
    }

    [Fact]
    public void A_reservation_with_an_active_cost_centre_and_a_budget_goes_to_the_grant() =>
        FundsCheck.RefusesReservation(Some.CostCentre(), Some.Budget(100m), out _).ShouldBeFalse();

    [Fact]
    public void A_commitment_for_a_released_requisition_is_refused_as_closed()
    {
        FundsCheck.RefusesCommitment(RequisitionReservation.Tombstone(Requisition), Some.Budget(100m), out FundsRefusal refusal)
            .ShouldBeTrue();
        refusal.ShouldBe(FundsRefusal.DocumentClosed);
    }

    [Fact]
    public void A_commitment_without_a_budget_is_refused_as_no_budget()
    {
        FundsCheck.RefusesCommitment(reservation: null, budget: null, out FundsRefusal refusal).ShouldBeTrue();
        refusal.ShouldBe(FundsRefusal.NoBudget);
    }

    [Fact]
    public void A_commitment_whose_requisition_was_never_seen_goes_to_the_grant_with_nothing_held() =>
        FundsCheck.RefusesCommitment(reservation: null, Some.Budget(100m), out _).ShouldBeFalse();

    [Fact]
    public void A_commitment_on_a_different_budget_from_its_reservation_is_a_broken_message()
    {
        RequisitionReservation reservation = RequisitionReservation.Hold(Requisition, Guid.CreateVersion7(), 50m);

        new Func<bool>(() => FundsCheck.RefusesCommitment(reservation, Some.Budget(100m), out _))
            .ShouldBreak("funds.budget_mismatch", ViolationKind.Invalid);
    }
}
