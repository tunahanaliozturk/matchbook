using System.Globalization;
using System.Numerics;

namespace Matchbook.Suppliers.UnitTests;

/// <summary>
/// Builds IBANs with correct check digits from a country and a BBAN. Computed the long way, with a
/// <see cref="BigInteger"/> over the whole expanded number, so it is an independent oracle for the digit-by-digit
/// remainder in <c>Iban</c> rather than a copy of it.
/// </summary>
internal static class TestIbans
{
    public const string German = "DE89370400440532013000";

    public static string Create(string country, string bban)
    {
        int checkDigits = 98 - Mod97(bban + country + "00");
        return string.Create(CultureInfo.InvariantCulture, $"{country}{checkDigits:00}{bban}");
    }

    /// <summary>A distinct valid German IBAN for each number.</summary>
    public static string Numbered(int number) =>
        Create("DE", number.ToString("D18", CultureInfo.InvariantCulture));

    /// <summary>The ISO 7064 remainder of an IBAN, rotated and expanded exactly as the standard describes.</summary>
    public static int Remainder(string iban) => Mod97(iban[4..] + iban[..4]);

    private static int Mod97(string text)
    {
        string digits = string.Concat(text.Select(static c => char.IsAsciiDigit(c)
            ? c.ToString()
            : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture)));

        return (int)(BigInteger.Parse(digits, CultureInfo.InvariantCulture) % 97);
    }
}
