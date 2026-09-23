using Matchbook.Payables.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

public sealed class BankDetailsTests
{
    [Theory]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("NL91ABNA0417164300")]
    [InlineData("NO9386011117947")]
    public void A_valid_iban_is_accepted(string value) => Iban.Parse(value).Value.ShouldBe(value);

    [Fact]
    public void An_iban_is_stored_upper_case_without_spaces() =>
        Iban.Parse("de89 3704 0044 0532 0130 00").Value.ShouldBe("DE89370400440532013000");

    [Theory]
    [InlineData("DE88370400440532013000")]
    [InlineData("DE8937040044053201300")]
    [InlineData("8937040044053201300DE0")]
    [InlineData("DE89-370400440532013000")]
    [InlineData("")]
    public void An_iban_with_a_wrong_check_or_shape_is_refused(string value) =>
        Should.Throw<BusinessRuleException>(() => Iban.Parse(value)).Code.ShouldBe("iban.invalid");

    [Fact]
    public void An_iban_prints_masked_so_a_log_line_cannot_leak_it()
    {
        Iban iban = Iban.Parse("DE89370400440532013000");

        iban.Masked.ShouldBe("****3000");
        iban.ToString().ShouldBe("****3000");
        $"{iban}".ShouldNotContain("DE89");
    }

    [Theory]
    [InlineData("COBADEFF")]
    [InlineData("COBADEFFXXX")]
    [InlineData("nwbkgb2l")]
    public void A_bic_of_eight_or_eleven_characters_is_accepted(string value) =>
        Bic.Parse(value).Value.ShouldBe(value.ToUpperInvariant());

    [Theory]
    [InlineData("COBADEF")]
    [InlineData("COBA1EFFXXX")]
    [InlineData("COBADEFFXX")]
    [InlineData("COBADEFF-XX")]
    public void A_bic_of_another_shape_is_refused(string value) =>
        Should.Throw<BusinessRuleException>(() => Bic.Parse(value)).Code.ShouldBe("bic.invalid");
}
