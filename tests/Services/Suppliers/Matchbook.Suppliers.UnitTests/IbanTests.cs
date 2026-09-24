using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

public sealed class IbanTests
{
    private const string IbanInvalid = "supplier.iban_invalid";

    [Theory]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB29NWBK60161331926819")]
    [InlineData("FR1420041010050500013M02606")]
    [InlineData("NL91ABNA0417164300")]
    [InlineData("BE68539007547034")]
    [InlineData("AT611904300234573201")]
    [InlineData("CH9300762011623852957")]
    [InlineData("ES9121000418450200051332")]
    [InlineData("IT60X0542811101000000123456")]
    [InlineData("NO9386011117947")]
    [InlineData("MT84MALT011000012345MTLCAST001S")]
    [InlineData("PL61109010140000071219812874")]
    [InlineData("SE4550000000058398257466")]
    [InlineData("FI2112345600000785")]
    [InlineData("IE29AIBK93115212345678")]
    [InlineData("PT50000201231234567890154")]
    [InlineData("DK5000400440116243")]
    public void A_published_example_iban_from_a_sepa_country_is_accepted(string iban) =>
        Iban.Parse(iban).Value.ShouldBe(iban);

    [Fact]
    public void Spaces_and_lower_case_are_normalised_away() =>
        Iban.Parse(" de89 3704 0044\t0532 0130 00 ").Value.ShouldBe("DE89370400440532013000");

    [Fact]
    public void Every_sepa_country_is_in_the_length_table_and_nothing_else_is()
    {
        string[] sepa =
        [
            "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV", "LT",
            "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "IS", "LI", "NO", "CH", "GB", "GI", "AD",
            "MC", "SM", "VA", "AL", "ME", "MD", "MK", "RS",
        ];

        Iban.Lengths.Keys.Order(StringComparer.Ordinal).ShouldBe(sepa.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_valid_iban_from_outside_sepa_is_refused()
    {
        string turkish = TestIbans.Create("TR", "0006100519786457841326");
        TestIbans.Remainder(turkish).ShouldBe(1);

        BrokenRule.Expect(IbanInvalid, ViolationKind.Invalid, () => Iban.Parse(turkish))
            .Message.ShouldContain("SEPA");
    }

    [Theory]
    [InlineData("DE8937040044053201300", "22 characters")]
    [InlineData("DE893704004405320130000", "22 characters")]
    [InlineData("DE88370400440532013000", "check digits")]
    [InlineData("DE89-3704-0044-0532-0130-00", "only letters and digits")]
    [InlineData("1289370400440532013000", "country code")]
    [InlineData("DE", "22 characters")]
    [InlineData("D", "country code")]
    [InlineData("DE8937040044053201300000000000000000", "at most 34")]
    public void A_malformed_iban_is_refused_with_the_reason(string iban, string reason) =>
        BrokenRule.Expect(IbanInvalid, ViolationKind.Invalid, () => Iban.Parse(iban)).Message.ShouldContain(reason);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_iban_is_required(string? iban) =>
        BrokenRule.Expect(IbanInvalid, ViolationKind.Invalid, () => Iban.Parse(iban));

    [Fact]
    public void Check_digits_outside_02_to_98_are_refused_even_where_mod_97_holds()
    {
        // Find a BBAN whose check digits come out as 02, then write 99, which is 02 plus 97.
        string valid = Enumerable.Range(0, 1000).Select(TestIbans.Numbered).First(iban => iban[2..4] == "02");
        string ninetyNine = string.Concat("DE99", valid.AsSpan(4));
        TestIbans.Remainder(ninetyNine).ShouldBe(1);

        BrokenRule.Expect(IbanInvalid, ViolationKind.Invalid, () => Iban.Parse(ninetyNine));
    }

    [Fact]
    public void An_error_message_never_repeats_the_iban()
    {
        const string typo = "DE88370400440532013000";

        BrokenRule.Expect(IbanInvalid, ViolationKind.Invalid, () => Iban.Parse(typo))
            .Message.ShouldNotContain("0532013000");
    }

    [Fact]
    public void Masking_shows_the_last_four_characters_only()
    {
        Iban iban = Iban.Parse(TestIbans.German);

        iban.Masked.ShouldBe("****3000");
        iban.ToString().ShouldBe("****3000");
    }

    [Fact]
    public void Two_spellings_of_one_iban_are_equal() =>
        Iban.Parse("de89 3704 0044 0532 0130 00").ShouldBe(Iban.Parse(TestIbans.German));

    // Properties over every SEPA country, with BBANs of mixed letters and digits and check digits computed by
    // an independent oracle.

    [Property]
    public Property Any_correctly_computed_iban_is_accepted_however_it_is_spaced_or_cased() =>
        Prop.ForAll(ValidIbans(), iban =>
        {
            string typed = string.Join(' ', iban.Chunk(4).Select(static group => new string(group))).ToLowerInvariant();

            Iban.Parse(typed).Value.ShouldBe(iban);
        });

    [Property]
    public Property Changing_any_one_digit_is_detected() =>
        Prop.ForAll(ValidIbans(), Arb.From(Gen.Choose(0, 1000)), Arb.From(Gen.Choose(1, 9)), (iban, pick, shift) =>
        {
            // The check digits are digits too, so there are always at least two positions to pick from.
            int[] digits = [.. Enumerable.Range(2, iban.Length - 2).Where(i => char.IsAsciiDigit(iban[i]))];
            int position = digits[pick % digits.Length];
            char[] typo = iban.ToCharArray();
            typo[position] = (char)('0' + ((typo[position] - '0' + shift) % 10));

            Should.Throw<BusinessRuleException>(() => Iban.Parse(new string(typo)));
        });

    [Property]
    public Property Swapping_two_adjacent_different_digits_is_detected() =>
        Prop.ForAll(ValidIbans(), Arb.From(Gen.Choose(0, 1000)), (iban, pick) =>
        {
            int[] swappable = [.. Enumerable.Range(2, iban.Length - 3)
                .Where(i => char.IsAsciiDigit(iban[i]) && char.IsAsciiDigit(iban[i + 1]) && iban[i] != iban[i + 1])];

            return Prop.When(swappable.Length > 0, () =>
            {
                int position = swappable[pick % swappable.Length];
                char[] typo = iban.ToCharArray();
                (typo[position], typo[position + 1]) = (typo[position + 1], typo[position]);

                Should.Throw<BusinessRuleException>(() => Iban.Parse(new string(typo)));
            });
        });

    [Property]
    public Property A_character_more_or_less_is_detected() =>
        Prop.ForAll(ValidIbans(), iban =>
        {
            Should.Throw<BusinessRuleException>(() => Iban.Parse(iban[..^1]));
            Should.Throw<BusinessRuleException>(() => Iban.Parse(iban + "0"));
        });

    [Property]
    public Property The_mask_has_one_width_and_ends_with_the_last_four_characters() =>
        Prop.ForAll(ValidIbans(), iban =>
        {
            string masked = Iban.Parse(iban).Masked;

            masked.ShouldBe("****" + iban[^4..]);
            masked.ShouldNotContain(iban[..^4]);
        });

    private static Arbitrary<string> ValidIbans()
    {
        Gen<char> bbanCharacter = Gen.Elements("01234567890123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());

        return Gen.Elements(Iban.Lengths.ToArray())
            .SelectMany(country => bbanCharacter.ArrayOf(country.Value - 4)
                .Select(bban => TestIbans.Create(country.Key, new string(bban))))
            .ToArbitrary();
    }
}
