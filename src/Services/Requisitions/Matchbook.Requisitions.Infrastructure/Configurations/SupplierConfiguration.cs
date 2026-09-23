using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(static supplier => supplier.Id);
        builder.Property(static supplier => supplier.Id).ValueGeneratedNever();

        // Same reason as the cost centre copy: an older snapshot must not overwrite a newer one it raced.
        builder.UseXminAsRowVersion();
    }
}
