using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal sealed class CostCentreConfiguration : IEntityTypeConfiguration<CostCentre>
{
    public void Configure(EntityTypeBuilder<CostCentre> builder)
    {
        builder.ToTable("cost_centres");
        builder.HasKey(static centre => centre.Code);
        builder.Property(static centre => centre.Code).HasMaxLength(CostCentre.MaxCodeLength).ValueGeneratedNever();

        // Two versions of one cost centre consumed at once both read the old row, and without a token the
        // older could commit last and win. With it the loser is retried, rereads, and finds itself stale.
        builder.UseXminAsRowVersion();
    }
}
