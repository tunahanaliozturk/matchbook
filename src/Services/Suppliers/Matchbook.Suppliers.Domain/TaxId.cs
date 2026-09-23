using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Domain;

/// <summary>
/// A tax id in the one spelling the unique index compares: upper case ASCII letters and digits, with spaces,
/// dots, dashes and every other separator dropped. <c>de 123.456.789</c> and <c>DE123456789</c> are one supplier.
/// </summary>
public sealed record TaxId
{
    public const int MinLength = 4;
    public const int MaxLength = 20;

    private TaxId(string value) => Value = value;

    public string Value { get; }

    public static TaxId Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw Invalid("A tax id is required.");
        }

        Span<char> normalised = stackalloc char[MaxLength];
        int length = 0;

        foreach (char c in input)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (length == MaxLength)
                {
                    throw Invalid($"A tax id has at most {MaxLength} letters and digits.");
                }

                normalised[length++] = char.ToUpperInvariant(c);
            }
            else if (char.IsLetterOrDigit(c))
            {
                // Dropping these as separators would let two different ids collide, and keeping them would let
                // a look-alike (a Cyrillic A, a full-width digit) slip past the unique index.
                throw Invalid("A tax id may contain only ASCII letters and digits besides separators.");
            }
        }

        return length >= MinLength
            ? new TaxId(new string(normalised[..length]))
            : throw Invalid($"A tax id has at least {MinLength} letters and digits.");
    }

    public override string ToString() => Value;

    private static BusinessRuleException Invalid(string message) =>
        new("supplier.tax_id_invalid", message, ViolationKind.Invalid);
}
