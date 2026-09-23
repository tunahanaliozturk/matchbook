namespace Matchbook.SharedKernel;

/// <summary>
/// One rounding rule for the whole system. Five services compute line amounts from quantities and prices, and
/// the reconciliation compares their totals to the cent, so they have to round the same way.
/// </summary>
/// <remarks>
/// Amounts are euros to two places, rounded half away from zero, as invoices are. Quantities carry up to three
/// places and unit prices up to four. There is one currency; see the known limitations.
/// </remarks>
public static class Amounts
{
    public const int AmountScale = 2;
    public const int QuantityScale = 3;
    public const int UnitPriceScale = 4;

    public static decimal Round(decimal amount) =>
        decimal.Round(amount, AmountScale, MidpointRounding.AwayFromZero);

    public static decimal Line(decimal quantity, decimal unitPrice) => Round(quantity * unitPrice);

    /// <summary>True when the value has no more decimal places than <paramref name="scale"/> allows.</summary>
    public static bool FitsScale(decimal value, int scale) => decimal.Round(value, scale) == value;
}
