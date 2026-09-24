using Matchbook.Payables.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers", table => table.HasCheckConstraint(
            "ck_suppliers_account_iban_protected",
            $"account_protected_iban IS NULL OR account_protected_iban LIKE '{BankColumns.ProtectedPrefix}%'"));
        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Id).ValueGeneratedNever();

        // Two snapshots applied at once must not both win, or an older one could land last.
        builder.Property<uint>(Columns.RowVersion).IsRowVersion();

        builder.Property(supplier => supplier.LegalName).HasMaxLength(200);

        builder.ComplexProperty(supplier => supplier.Account, account =>
        {
            account.Property(a => a.AccountVersion).HasColumnName("account_version");
            account.Property(a => a.ProtectedIban).HasColumnName("account_protected_iban");
            account.Property(a => a.IbanLastFour).HasColumnName("account_iban_last_four").HasMaxLength(4);
            account.Property(a => a.Bic).HasColumnName("account_bic").IsBic();
            account.Property(a => a.AccountHolder).HasColumnName("account_holder").HasMaxLength(200);
        });
    }
}
