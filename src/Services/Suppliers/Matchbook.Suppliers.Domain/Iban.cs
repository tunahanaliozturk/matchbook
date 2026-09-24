using System.Collections.Frozen;
using System.Globalization;
using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Domain;

/// <summary>
/// An IBAN from a country in the SEPA schemes, in its electronic form: no spaces, upper case. Checked for the
/// country's length and for the ISO 7064 mod 97-10 check digits.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> returns the masked form, so an IBAN that ends up in a log line or an exception message
/// by accident shows four characters, not the account. Read <see cref="Value"/> when the full number is needed.
/// </remarks>
public sealed record Iban
{
    private const int MaxLength = 34;
    private const int VisibleCharacters = 4;

    // Only SEPA scheme countries, per the EPC list of 2025. Payables pays by SEPA credit transfer
    // (pain.001), which cannot reach an account elsewhere, so a Turkish or US IBAN is refused here rather than
    // at payment time. Guernsey, Jersey, the Isle of Man, the French overseas departments and the Åland
    // Islands are in SEPA but issue GB, FR and FI IBANs.
    private static readonly FrozenDictionary<string, int> SepaLengths = string.Join(' ',
            "AD24 AL28 AT20 BE16 BG22 CH21 CY28 CZ24 DE22 DK18 EE20 ES24 FI18 FR27",
            "GB22 GI23 GR27 HR21 HU28 IE22 IS26 IT27 LI21 LT20 LU20 LV21 MC27 MD24",
            "ME22 MK19 MT31 NL18 NO15 PL28 PT25 RO24 RS22 SE24 SI19 SK24 SM27 VA22")
        .Split(' ')
        .ToFrozenDictionary(
            static entry => entry[..2],
            static entry => int.Parse(entry.AsSpan(2), CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

    private Iban(string value) => Value = value;

    public string Value { get; }

    /// <summary>The last four characters behind a fixed-width prefix, so the mask does not give away the length.</summary>
    public string Masked => string.Concat("****", Value.AsSpan(Value.Length - VisibleCharacters));

    internal static IReadOnlyDictionary<string, int> Lengths => SepaLengths;

    // No message below repeats the input: exception messages reach logs, and an IBAN must not.
    public static Iban Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw Invalid("An IBAN is required.");
        }

        Span<char> iban = stackalloc char[MaxLength];
        int length = 0;

        foreach (char c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c))
            {
                throw Invalid("An IBAN contains only letters and digits.");
            }

            if (length == MaxLength)
            {
                throw Invalid($"An IBAN has at most {MaxLength} characters.");
            }

            iban[length++] = char.ToUpperInvariant(c);
        }

        string value = new(iban[..length]);

        if (length < 2 || !char.IsAsciiLetter(value[0]) || !char.IsAsciiLetter(value[1]))
        {
            throw Invalid("An IBAN starts with a two-letter country code.");
        }

        if (!SepaLengths.TryGetValue(value[..2], out int expected))
        {
            throw Invalid("Only IBANs from SEPA countries are accepted: payments go out as SEPA credit transfers.");
        }

        if (length != expected)
        {
            throw Invalid($"An IBAN from {value[..2]} has {expected} characters.");
        }

        if (!HasValidCheckDigits(value))
        {
            throw Invalid("The IBAN's check digits do not match. Check it for a typing mistake.");
        }

        return new Iban(value);
    }

    public override string ToString() => Masked;

    private static bool HasValidCheckDigits(string iban)
    {
        // Check digits are 02 to 98 by construction. 00, 01 and 99 can still satisfy mod 97 (99 is 2 more than
        // 97), so they are refused before the arithmetic.
        if (!char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
        {
            return false;
        }

        int checkDigits = ((iban[2] - '0') * 10) + (iban[3] - '0');
        if (checkDigits is < 2 or > 98)
        {
            return false;
        }

        // ISO 7064 mod 97-10 over the IBAN rotated by four, letters read as 10 to 35, one digit at a time so
        // the remainder never outgrows an int.
        int remainder = 0;
        for (int i = 0; i < iban.Length; i++)
        {
            char c = iban[(i + 4) % iban.Length];
            remainder = char.IsAsciiDigit(c)
                ? ((remainder * 10) + (c - '0')) % 97
                : ((remainder * 100) + (c - 'A' + 10)) % 97;
        }

        return remainder == 1;
    }

    private static BusinessRuleException Invalid(string message) =>
        new("supplier.iban_invalid", message, ViolationKind.Invalid);
}
