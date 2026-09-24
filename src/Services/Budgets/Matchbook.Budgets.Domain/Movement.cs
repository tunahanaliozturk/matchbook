namespace Matchbook.Budgets.Domain;

/// <summary>
/// A change to a budget's four figures. Every ledger entry carries one, and a budget's figures are the sum of
/// the movements in its ledger. The factories validate the amounts that come from outside, so a movement built
/// from a malformed message fails before anything is applied.
/// </summary>
public readonly record struct Movement(decimal Allotted, decimal Reserved, decimal Committed, decimal Actual)
{
    /// <summary>How much of the available figure this movement uses up. Negative when it frees funds.</summary>
    public decimal Consumes => Reserved + Committed + Actual - Allotted;

    public static Movement Allot(decimal change) => new(Money.Valid(change, "The allotment change"), 0m, 0m, 0m);

    public static Movement Reserve(decimal amount) => new(0m, Money.Positive(amount, "The requisition amount"), 0m, 0m);

    public static Movement ReleaseReservation(decimal amount) => new(0m, -amount, 0m, 0m);

    /// <summary>The requisition's reservation leaves and the order's amount arrives, in one step.</summary>
    public static Movement Commit(decimal reservationHeld, decimal orderAmount) =>
        new(0m, -Money.NotNegative(reservationHeld, "The reservation"), Money.Positive(orderAmount, "The order amount"), 0m);

    public static Movement ReleaseCommitment(decimal amount) => new(0m, 0m, -amount, 0m);

    /// <summary>
    /// An invoice relieves <paramref name="relief"/> of the order's commitment and adds its whole
    /// <paramref name="amount"/> to actual. When the invoice is larger than what was still committed, the
    /// difference is new spending, and it lands in actual too.
    /// </summary>
    public static Movement Invoice(decimal relief, decimal amount) => new(0m, 0m, -relief, amount);
}
