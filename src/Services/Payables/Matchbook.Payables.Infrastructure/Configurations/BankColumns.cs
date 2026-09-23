using Matchbook.Payables.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchbook.Payables.Infrastructure.Configurations;

/// <summary>
/// How bank details are stored. Every IBAN column is mapped here, so encrypting them at rest changes this file and
/// nothing in the domain or the handlers.
/// </summary>
internal static class BankColumns
{
    private static readonly ValueConverter<Iban, string> IbanAsText = new(iban => iban.Value, value => Iban.Parse(value));

    private static readonly ValueConverter<Bic, string> BicAsText = new(bic => bic.Value, value => Bic.Parse(value));

    public static void IsIban(this PropertyBuilder<Iban> property) => property.HasConversion(IbanAsText);

    public static void IsIban(this ComplexTypePropertyBuilder<Iban> property) => property.HasConversion(IbanAsText);

    public static void IsBic(this PropertyBuilder<Bic> property) => property.HasConversion(BicAsText).HasMaxLength(11);

    public static void IsBic(this ComplexTypePropertyBuilder<Bic> property) => property.HasConversion(BicAsText).HasMaxLength(11);
}
