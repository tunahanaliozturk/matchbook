using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain;

/// <summary>
/// An international bank account number, stored upper case without spaces, checked by shape and mod 97.
/// </summary>
/// <remarks>
/// Suppliers validates accounts before it verifies them, so this is a second look rather than the gate. It is its
/// own type for two reasons: persistence maps it in one place, which is where encryption at rest plugs in, and
/// <see cref="ToString"/> masks it, so a log line that formats one by accident does not leak it.
/// </remarks>
public sealed record Iban
{
    private Iban(string value) => Value = value;

    public string Value { get; }

    /// <summary>The last four characters behind a mask, which is all a response or a log line should show.</summary>
    public string Masked => string.Concat("****", Value.AsSpan(Value.Length - 4));

    public static Iban Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string compact = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        return IsValid(compact)
            ? new Iban(compact)
            : throw new BusinessRuleException("iban.invalid", "The IBAN is not valid.", ViolationKind.Invalid);
    }

    public override string ToString() => Masked;

    private static bool IsValid(string iban)
    {
        if (iban.Length is < 15 or > 34
            || !char.IsAsciiLetterUpper(iban[0]) || !char.IsAsciiLetterUpper(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
        {
            return false;
        }

        // ISO 13616: move the first four characters to the end, read letters as 10 to 35, and the number mod 97
        // must be 1. Folding digit by digit keeps it in an int however long the account number is.
        int remainder = 0;
        for (int i = 0; i < iban.Length; i++)
        {
            char c = iban[(i + 4) % iban.Length];
            if (char.IsAsciiDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else if (char.IsAsciiLetterUpper(c))
            {
                remainder = ((remainder * 100) + (c - 'A' + 10)) % 97;
            }
            else
            {
                return false;
            }
        }

        return remainder == 1;
    }
}
