using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

public sealed class TaxIdTests
{
    private const string TaxIdInvalid = "supplier.tax_id_invalid";

    [Theory]
    [InlineData("DE123456789", "DE123456789")]
    [InlineData("de 123.456.789", "DE123456789")]
    [InlineData("NL-8547.29.345-B01", "NL854729345B01")]
    [InlineData("  1234  ", "1234")]
    public void Separators_and_case_are_normalised_away(string input, string normalised) =>
        TaxId.Parse(input).Value.ShouldBe(normalised);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" - . ")]
    [InlineData("AB1")]
    [InlineData("A-B-1")]
    [InlineData("ABCDEFGHIJ12345678901")]
    public void Fewer_than_four_or_more_than_twenty_letters_and_digits_are_refused(string? input) =>
        BrokenRule.Expect(TaxIdInvalid, ViolationKind.Invalid, () => TaxId.Parse(input));

    [Theory]
    [InlineData("DE12345678É")] // A Latin E with an acute accent.
    [InlineData("DЕ123456789")] // A Cyrillic Ie, which looks like an E.
    [InlineData("DE１２３４５")] // Full-width digits.
    public void Letters_and_digits_outside_ascii_are_refused_rather_than_dropped(string input) =>
        BrokenRule.Expect(TaxIdInvalid, ViolationKind.Invalid, () => TaxId.Parse(input));

    [Property]
    public Property However_an_id_is_typed_it_normalises_to_the_same_value() =>
        Prop.ForAll(Ids(), Arb.From(Gen.Elements(" ", ".", "-", "/", "  ").ArrayOf()), Arb.From(Gen.Choose(0, 1000)),
            (id, separators, seed) =>
            {
                var random = new Random(seed);
                string typed = string.Concat(id.Select((c, i) =>
                    (random.Next(2) == 0 ? char.ToLowerInvariant(c) : c)
                    + (i < separators.Length ? separators[i] : string.Empty)));

                TaxId.Parse(typed).Value.ShouldBe(id);
            });

    [Property]
    public Property Normalising_twice_changes_nothing() =>
        Prop.ForAll(Ids(), id => TaxId.Parse(TaxId.Parse(id).Value) == TaxId.Parse(id));

    private static Arbitrary<string> Ids() =>
        Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray())
            .ArrayOf()
            .Where(static chars => chars.Length is >= TaxId.MinLength and <= TaxId.MaxLength)
            .Select(static chars => new string(chars))
            .ToArbitrary();
}
