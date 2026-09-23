using System.Buffers;
using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Domain;

/// <summary>
/// A business identifier code (ISO 9362): four letters for the bank, an assigned country code, two letters or
/// digits for the location, and an optional three for the branch.
/// </summary>
public sealed record Bic
{
    private static readonly SearchValues<char> LettersAndDigits =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");

    private Bic(string value) => Value = value;

    public string Value { get; }

    public static Bic Parse(string? input)
    {
        string bic = string.Concat((input ?? string.Empty).Where(static c => !char.IsWhiteSpace(c)))
            .ToUpperInvariant();

        bool valid = bic.Length is 8 or 11
            && !bic.AsSpan(0, 4).ContainsAnyExceptInRange('A', 'Z')
            && CountryCode.IsAssigned(bic[4..6])
            && !bic.AsSpan(6).ContainsAnyExcept(LettersAndDigits);

        return valid
            ? new Bic(bic)
            : throw new BusinessRuleException(
                "supplier.bic_invalid",
                "A BIC has 8 or 11 letters and digits: bank, country, location and an optional branch.",
                ViolationKind.Invalid);
    }

    public override string ToString() => Value;
}
