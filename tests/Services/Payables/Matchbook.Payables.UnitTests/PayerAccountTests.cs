using Matchbook.Payables.Application.Features.PaymentRuns;

namespace Matchbook.Payables.UnitTests;

public sealed class PayerAccountTests
{
    private static readonly PayerAccount Valid = new() { Name = "Matchbook Demo GmbH", Iban = "DE89 3704 0044 0532 0130 00", Bic = "COBADEFFXXX" };

    [Fact]
    public void A_named_account_with_a_valid_iban_and_bic_is_accepted() => Valid.IsValid().ShouldBeTrue();

    [Theory]
    [InlineData("", "DE89370400440532013000", "COBADEFFXXX")]
    [InlineData("Matchbook", "DE88370400440532013000", "COBADEFFXXX")]
    [InlineData("Matchbook", "DE89370400440532013000", "COBA")]
    public void A_missing_name_or_a_malformed_iban_or_bic_is_refused(string name, string iban, string bic) =>
        (Valid with { Name = name, Iban = iban, Bic = bic }).IsValid().ShouldBeFalse();

    [Fact]
    public void The_account_number_never_appears_when_the_account_is_printed() =>
        Valid.ToString().ShouldNotContain("3704");
}
