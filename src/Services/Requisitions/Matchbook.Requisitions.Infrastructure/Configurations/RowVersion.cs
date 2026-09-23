using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal static class RowVersion
{
    /// <summary>
    /// Maps Postgres' <c>xmin</c> system column as the concurrency token, as a shadow property so the domain
    /// carries no persistence field. Every UPDATE checks it, so of two writers who read the same row the
    /// second gets a concurrency conflict instead of overwriting the first.
    /// </summary>
    public static void UseXminAsRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<uint>("RowVersion").HasColumnName("xmin").IsRowVersion();
}
