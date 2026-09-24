using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Domain;

/// <summary>
/// Checks that a quantity, a unit price and the amount they make fit the columns they are stored in:
/// <c>numeric(18,3)</c>, <c>numeric(18,4)</c> and <c>numeric(18,2)</c>.
/// </summary>
/// <remarks>
/// Postgres rounds surplus decimal places without a word and refuses surplus integer digits with an error, so a
/// value that does not fit would either change silently or fail as a 500. Both are turned away here, with a code
/// the client can act on, before anything is stored.
/// </remarks>
internal static class LineValues
{
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private const decimal MaxQuantity = 999_999_999_999_999.999m;
    private const decimal MaxUnitPrice = 99_999_999_999_999.9999m;

    public static void EnsureQuantity(decimal quantity)
    {
        if (quantity < 0)
        {
            throw new BusinessRuleException(
                "purchase_order.negative_quantity", "A quantity cannot be below zero.", ViolationKind.Invalid);
        }

        if (quantity > MaxQuantity || !Amounts.FitsScale(quantity, Amounts.QuantityScale))
        {
            throw new BusinessRuleException(
                "purchase_order.quantity_precision",
                "A quantity has at most three decimal places and fifteen digits before the point.",
                ViolationKind.Invalid);
        }
    }

    public static void EnsureUnitPrice(decimal unitPrice)
    {
        if (unitPrice < 0)
        {
            throw new BusinessRuleException(
                "purchase_order.negative_unit_price", "A unit price cannot be below zero.", ViolationKind.Invalid);
        }

        if (unitPrice > MaxUnitPrice || !Amounts.FitsScale(unitPrice, Amounts.UnitPriceScale))
        {
            throw new BusinessRuleException(
                "purchase_order.unit_price_precision",
                "A unit price has at most four decimal places and fourteen digits before the point.",
                ViolationKind.Invalid);
        }
    }

    /// <summary>Validates both inputs and returns the line amount, rounded the way every service rounds it.</summary>
    public static decimal LineAmount(decimal quantity, decimal unitPrice)
    {
        EnsureQuantity(quantity);
        EnsureUnitPrice(unitPrice);

        // Checked by division first: at the top of both ranges the product overflows decimal itself.
        if (unitPrice > 0 && quantity > MaxAmount / unitPrice)
        {
            throw AmountTooLarge();
        }

        return Amounts.Line(quantity, unitPrice);
    }

    public static BusinessRuleException AmountTooLarge() =>
        new("purchase_order.amount_too_large", "The amount is larger than an order can hold.", ViolationKind.Invalid);
}
