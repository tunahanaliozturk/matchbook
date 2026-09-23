using Matchbook.Payables.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Id).ValueGeneratedNever();

        // Two snapshots applied at once must not both win, or an older one could land last.
        builder.Property<uint>(Columns.RowVersion).IsRowVersion();

        builder.Property(supplier => supplier.LegalName).HasMaxLength(200);

        builder.ComplexProperty(supplier => supplier.Account, account =>
        {
            account.Property(a => a.AccountVersion).HasColumnName("account_version");
            account.Property(a => a.Iban).HasColumnName("account_iban").IsIban();
            account.Property(a => a.Bic).HasColumnName("account_bic").IsBic();
            account.Property(a => a.AccountHolder).HasColumnName("account_holder").HasMaxLength(200);
        });
    }
}
