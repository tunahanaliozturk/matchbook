using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal sealed class CostCentreConfiguration : IEntityTypeConfiguration<CostCentre>
{
    public void Configure(EntityTypeBuilder<CostCentre> entity)
    {
        entity.ToTable("cost_centres", table =>
        {
            table.HasCheckConstraint("ck_cost_centres_code_format", "code ~ '^[A-Z]{2,5}-[A-Z0-9]{2,12}$'");
            table.HasCheckConstraint("ck_cost_centres_version_positive", "version >= 1");
        });

        entity.HasKey(c => c.Code);
        entity.Property(c => c.Code).HasMaxLength(CostCentre.CodeMaxLength);
        entity.Property(c => c.Name).HasMaxLength(CostCentre.NameMaxLength);
        entity.HasXminToken();
    }
}
