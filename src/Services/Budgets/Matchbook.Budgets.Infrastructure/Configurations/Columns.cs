using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal static class Columns
{
    public const int EnumLength = 24;

    /// <summary>Euros to two places, as every service stores money.</summary>
    public static PropertyBuilder<decimal> IsMoney(this PropertyBuilder<decimal> property) => property.HasPrecision(18, 2);

    /// <summary>Stored by name, so the database reads the same as the code and a reordered enum breaks nothing.</summary>
    public static PropertyBuilder<TEnum> IsName<TEnum>(this PropertyBuilder<TEnum> property) =>
        property.HasConversion<string>().HasMaxLength(EnumLength);

    /// <summary>
    /// Postgres's xmin system column as an optimistic concurrency token, for rows that two handlers may load and
    /// change at the same time. It is not a real column, so no migration creates it.
    /// </summary>
    public static void HasXminToken<T>(this EntityTypeBuilder<T> entity)
        where T : class =>
        entity.Property<uint>("xmin").IsRowVersion();
}
