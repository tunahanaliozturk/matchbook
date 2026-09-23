using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>
/// How far an invoiced unit price may stray from the ordered one before a person has to look: the variance on a
/// line may not exceed 2% of the ordered line amount, or 100.00, whichever is smaller.
/// </summary>
/// <remarks>
/// The percentage keeps small lines honest and the cap keeps large ones from hiding real money in a rounding-sized
/// share. Both sides are compared exactly, with no rounding of the variance, so the boundary is where the rule says
/// it is: a variance equal to the allowance passes, anything above it does not.
/// </remarks>
public static class PriceTolerance
{
    public const decimal Rate = 0.02m;

    public const decimal Cap = 100.00m;

    /// <summary><c>|invoiced - ordered| x quantity</c>, unrounded.</summary>
    public static decimal Variance(decimal quantity, decimal invoicedUnitPrice, decimal orderedUnitPrice) =>
        Math.Abs(invoicedUnitPrice - orderedUnitPrice) * quantity;

    /// <summary>The largest variance allowed on a line of <paramref name="quantity"/> at the ordered price.</summary>
    public static decimal Allowed(decimal quantity, decimal orderedUnitPrice) =>
        Math.Min(Rate * Amounts.Line(quantity, orderedUnitPrice), Cap);
}
