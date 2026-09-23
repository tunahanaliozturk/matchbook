using System.Buffers;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain;

/// <summary>
/// A bank identifier code in the shape pain.001 accepts (<c>BICFI</c>): four bank characters, a two-letter country,
/// two location characters and an optional three-character branch.
/// </summary>
public sealed record Bic
{
    private static readonly SearchValues<char> Alphanumerics =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");

    private Bic(string value) => Value = value;

    public string Value { get; }

    public static Bic Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string compact = value.Trim().ToUpperInvariant();

        return IsValid(compact)
            ? new Bic(compact)
            : throw new BusinessRuleException("bic.invalid", "The BIC is not valid.", ViolationKind.Invalid);
    }

    public override string ToString() => Value;

    private static bool IsValid(string bic) =>
        bic.Length is 8 or 11
        && !bic.AsSpan(0, 4).ContainsAnyExcept(Alphanumerics)
        && char.IsAsciiLetterUpper(bic[4])
        && char.IsAsciiLetterUpper(bic[5])
        && !bic.AsSpan(6).ContainsAnyExcept(Alphanumerics);
}
