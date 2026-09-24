using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

public sealed class CountryAndBicTests
{
    [Fact]
    public void Every_officially_assigned_iso_3166_code_is_known() =>
        CountryCode.AssignedCount.ShouldBe(249);

    [Theory]
    [InlineData("DE", "DE")]
    [InlineData(" nl ", "NL")]
    [InlineData("gb", "GB")]
    public void A_country_code_is_trimmed_and_upper_cased(string input, string code) =>
        CountryCode.Parse(input).Value.ShouldBe(code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("EU")]
    [InlineData("UK")]
    [InlineData("D")]
    [InlineData("DEU")]
    public void A_code_that_is_not_an_assigned_country_is_refused(string? input) =>
        BrokenRule.Expect("supplier.country_invalid", ViolationKind.Invalid, () => CountryCode.Parse(input));

    [Theory]
    [InlineData("DEUTDEFF", "DEUTDEFF")]
    [InlineData("deut de ff 500", "DEUTDEFF500")]
    [InlineData("NWBKGB2L", "NWBKGB2L")]
    [InlineData("ABNANL2A", "ABNANL2A")]
    public void A_bic_of_eight_or_eleven_characters_is_accepted_and_normalised(string input, string bic) =>
        Bic.Parse(input).Value.ShouldBe(bic);

    [Theory]
    [InlineData(null)]
    [InlineData("DEUTDEF")]
    [InlineData("DEUTDEFF5")]
    [InlineData("DEUTDEFF5000")]
    [InlineData("DEU1DEFF")]
    [InlineData("DEUTXXFF")]
    [InlineData("DEUTDEF-")]
    [InlineData("DEUTDEFF50-")]
    public void A_bic_with_the_wrong_length_or_shape_is_refused(string? input) =>
        BrokenRule.Expect("supplier.bic_invalid", ViolationKind.Invalid, () => Bic.Parse(input));
}
