using Matchbook.Payables.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchbook.Payables.Infrastructure.Configurations;

/// <summary>How bank details are stored.</summary>
/// <remarks>
/// Account numbers are stored as the ciphertext Suppliers sent, never decrypted on the way in or out of the
/// database. A check constraint on each such column demands the protected format, so a plain IBAN written by
/// mistake is refused by the database rather than found in an audit.
/// </remarks>
internal static class BankColumns
{
    /// <summary>What every value <c>ColumnProtector</c> writes starts with.</summary>
    public const string ProtectedPrefix = "v1.";

    private static readonly ValueConverter<Bic, string> BicAsText = new(bic => bic.Value, value => Bic.Parse(value));

    public static void IsBic(this PropertyBuilder<Bic> property) => property.HasConversion(BicAsText).HasMaxLength(11);

    public static void IsBic(this ComplexTypePropertyBuilder<Bic> property) => property.HasConversion(BicAsText).HasMaxLength(11);
}
