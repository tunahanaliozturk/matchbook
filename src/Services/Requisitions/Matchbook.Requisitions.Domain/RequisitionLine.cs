using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Domain;

/// <summary>One thing to buy, numbered from 1 in the order the requester listed them.</summary>
public sealed class RequisitionLine
{
    public const int MaxDescriptionLength = 500;
    public const int MaxUnitOfMeasureLength = 16;

    // The largest values the numeric(18,3) and numeric(18,4) columns hold. Checking here turns an overflow
    // the database would report as a 500 into a refusal the requester can act on.
    public const decimal MaxQuantity = 999_999_999_999_999.999m;
    public const decimal MaxUnitPrice = 99_999_999_999_999.9999m;

    private RequisitionLine(
        int lineNumber,
        string description,
        decimal quantity,
        string unitOfMeasure,
        decimal unitPrice,
        decimal amount)
    {
        LineNumber = lineNumber;
        Description = description;
        Quantity = quantity;
        UnitOfMeasure = unitOfMeasure;
        UnitPrice = unitPrice;
        Amount = amount;
    }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public string UnitOfMeasure { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Checks one line and computes its amount, without touching any requisition.</summary>
    internal static RequisitionLine Validated(int lineNumber, LineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string description = Text(input.Description, MaxDescriptionLength)
            ?? throw Invalid(lineNumber, $"needs a description of at most {MaxDescriptionLength} characters");
        string unitOfMeasure = Text(input.UnitOfMeasure, MaxUnitOfMeasureLength)
            ?? throw Invalid(lineNumber, $"needs a unit of measure of at most {MaxUnitOfMeasureLength} characters");

        if (input.Quantity <= 0 || input.Quantity > MaxQuantity || !Amounts.FitsScale(input.Quantity, Amounts.QuantityScale))
        {
            throw Invalid(lineNumber, $"needs a quantity above zero with at most {Amounts.QuantityScale} decimal places");
        }

        if (input.UnitPrice < 0 || input.UnitPrice > MaxUnitPrice || !Amounts.FitsScale(input.UnitPrice, Amounts.UnitPriceScale))
        {
            throw Invalid(lineNumber, $"needs a unit price of zero or more with at most {Amounts.UnitPriceScale} decimal places");
        }

        // Divide before multiplying: the largest quantity times the largest price overflows decimal itself.
        if (input.UnitPrice > 0 && input.Quantity > Requisition.MaxAmount / input.UnitPrice)
        {
            throw TooLarge(lineNumber);
        }

        decimal amount = Amounts.Line(input.Quantity, input.UnitPrice);
        if (amount > Requisition.MaxAmount)
        {
            throw TooLarge(lineNumber);
        }

        return new RequisitionLine(lineNumber, description, input.Quantity, unitOfMeasure, input.UnitPrice, amount);
    }

    internal void CopyFrom(RequisitionLine other)
    {
        Description = other.Description;
        Quantity = other.Quantity;
        UnitOfMeasure = other.UnitOfMeasure;
        UnitPrice = other.UnitPrice;
        Amount = other.Amount;
    }

    private static string? Text(string? value, int maxLength)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength ? null : trimmed;
    }

    private static BusinessRuleException Invalid(int lineNumber, string problem) =>
        new(RequisitionCodes.LineInvalid, $"Line {lineNumber} {problem}.", ViolationKind.Invalid);

    private static BusinessRuleException TooLarge(int lineNumber) =>
        new(RequisitionCodes.AmountTooLarge, $"Line {lineNumber} comes to more than {Requisition.MaxAmount}.", ViolationKind.Invalid);
}
