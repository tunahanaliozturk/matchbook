using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Domain;

/// <summary>
/// The checks every euro amount passes before it touches a budget. Figures are stored as numeric(18,2), so an
/// amount with a third decimal place, or more than sixteen digits before the point, is refused here with a code
/// instead of being rounded or rejected by Postgres halfway through a transaction.
/// </summary>
public static class Money
{
    // numeric(18,2) leaves sixteen digits before the point.
    private const decimal Ceiling = 10_000_000_000_000_000m;

    public static decimal Positive(decimal amount, string name) =>
        amount > 0m ? Valid(amount, name) : throw Invalid(name, "must be greater than zero");

    public static decimal NotNegative(decimal amount, string name) =>
        amount >= 0m ? Valid(amount, name) : throw Invalid(name, "cannot be negative");

    public static decimal Valid(decimal amount, string name) =>
        Math.Abs(amount) < Ceiling && Amounts.FitsScale(amount, Amounts.AmountScale)
            ? amount
            : throw Invalid(name, "must have at most two decimal places and sixteen digits before the point");

    private static BusinessRuleException Invalid(string name, string rule) =>
        new("budget.amount_invalid", $"{name} {rule}.", ViolationKind.Invalid);
}
