namespace Matchbook.Budgets.Domain;

public enum ReservationStatus
{
    /// <summary>Funds are set aside on the budget.</summary>
    Held,

    /// <summary>Budgets said no. Kept so a redelivered submission gets the same answer, not a second try.</summary>
    Refused,

    /// <summary>
    /// Rejected, cancelled, or closed with its order. Also the tombstone a release leaves when it arrives before
    /// the reservation it releases; a reservation that turns up afterwards is refused.
    /// </summary>
    Released,

    /// <summary>A purchase order took the reservation over as its commitment.</summary>
    Committed,
}

/// <summary>
/// What Budgets knows about one requisition's funds. It is created by whichever message about the requisition
/// arrives first, so the facts survive any delivery order.
/// </summary>
public sealed class RequisitionReservation
{
    private RequisitionReservation(
        Guid requisitionId,
        Guid? budgetId,
        decimal amount,
        ReservationStatus status,
        FundsRefusal? refusal)
    {
        RequisitionId = requisitionId;
        BudgetId = budgetId;
        Amount = amount;
        Status = status;
        Refusal = refusal;
    }

    public Guid RequisitionId { get; private set; }

    /// <summary>Unknown for a tombstone: a release does not say which budget the requisition would have used.</summary>
    public Guid? BudgetId { get; private set; }

    /// <summary>The amount requested. It stays on record after the reservation is released or handed over.</summary>
    public decimal Amount { get; private set; }

    public ReservationStatus Status { get; private set; }

    public FundsRefusal? Refusal { get; private set; }

    /// <summary>What the budget holds for this requisition right now.</summary>
    public decimal Held => Status == ReservationStatus.Held ? Amount : 0m;

    public static RequisitionReservation Hold(Guid requisitionId, Guid budgetId, decimal amount) =>
        new(requisitionId, budgetId, Money.Positive(amount, "The requisition amount"), ReservationStatus.Held, refusal: null);

    public static RequisitionReservation Refuse(Guid requisitionId, decimal amount, FundsRefusal reason) =>
        new(requisitionId, budgetId: null, amount, ReservationStatus.Refused, reason);

    public static RequisitionReservation Tombstone(Guid requisitionId) =>
        new(requisitionId, budgetId: null, 0m, ReservationStatus.Released, refusal: null);

    /// <summary>
    /// Gives the funds back. Returns the ledger entry to record, or null when nothing was held: a second
    /// release, or a release of a reservation that was refused or already handed to an order.
    /// </summary>
    public LedgerEntry? Release(DateTimeOffset occurredAt, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Held || BudgetId is not { } budgetId)
        {
            return null;
        }

        Status = ReservationStatus.Released;
        return LedgerEntry.Record(
            budgetId, RequisitionId, LedgerStep.Release, Movement.ReleaseReservation(Amount), occurredAt, now);
    }

    /// <summary>The order's commitment entry has taken the reservation over; nothing is held here any more.</summary>
    public void HandOver()
    {
        if (Status == ReservationStatus.Held)
        {
            Status = ReservationStatus.Committed;
        }
    }
}
