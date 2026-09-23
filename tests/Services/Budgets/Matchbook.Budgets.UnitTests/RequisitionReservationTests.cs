using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.UnitTests;

public sealed class RequisitionReservationTests
{
    private static readonly Guid Requisition = Guid.CreateVersion7();

    [Fact]
    public void Releasing_a_held_reservation_gives_the_whole_amount_back_once()
    {
        RequisitionReservation reservation = RequisitionReservation.Hold(Requisition, Some.BudgetId, 750m);

        LedgerEntry? entry = reservation.Release(Some.Now, Some.Now);

        entry.ShouldNotBeNull();
        entry.DocumentId.ShouldBe(Requisition);
        entry.Step.ShouldBe(LedgerStep.Release);
        entry.Movement.ShouldBe(Movement.ReleaseReservation(750m));
        reservation.Status.ShouldBe(ReservationStatus.Released);
        reservation.Held.ShouldBe(0m);
        reservation.Release(Some.Now, Some.Now).ShouldBeNull();
    }

    [Fact]
    public void A_tombstone_holds_nothing_and_counts_as_released()
    {
        RequisitionReservation tombstone = RequisitionReservation.Tombstone(Requisition);

        tombstone.Status.ShouldBe(ReservationStatus.Released);
        tombstone.BudgetId.ShouldBeNull();
        tombstone.Held.ShouldBe(0m);
        tombstone.Release(Some.Now, Some.Now).ShouldBeNull();
    }

    [Fact]
    public void Releasing_a_refused_reservation_records_nothing()
    {
        RequisitionReservation refused =
            RequisitionReservation.Refuse(Requisition, 750m, FundsRefusal.InsufficientFunds);

        refused.Release(Some.Now, Some.Now).ShouldBeNull();
        refused.Status.ShouldBe(ReservationStatus.Refused);
        refused.Refusal.ShouldBe(FundsRefusal.InsufficientFunds);
    }

    [Fact]
    public void A_reservation_handed_to_an_order_holds_nothing_and_cannot_be_released()
    {
        RequisitionReservation reservation = RequisitionReservation.Hold(Requisition, Some.BudgetId, 750m);

        reservation.HandOver();

        reservation.Status.ShouldBe(ReservationStatus.Committed);
        reservation.Held.ShouldBe(0m);
        reservation.Amount.ShouldBe(750m);
        reservation.Release(Some.Now, Some.Now).ShouldBeNull();
    }
}
