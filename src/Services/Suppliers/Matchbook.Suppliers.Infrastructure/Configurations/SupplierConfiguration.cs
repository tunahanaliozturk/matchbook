using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Suppliers.Infrastructure.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        // Each rule the domain enforces that a stray UPDATE could still break is repeated here, so the database
        // refuses a row the domain would never have produced.
        builder.ToTable("suppliers", static table =>
        {
            table.HasCheckConstraint(
                "ck_suppliers_status", "status IN ('Draft', 'PendingActivation', 'Active', 'Blocked')");
            table.HasCheckConstraint("ck_suppliers_tax_id_normalised", "tax_id ~ '^[A-Z0-9]{4,20}$'");
            table.HasCheckConstraint("ck_suppliers_country", "country ~ '^[A-Z]{2}$'");
            table.HasCheckConstraint("ck_suppliers_payment_terms_days", "payment_terms_days BETWEEN 0 AND 120");
            table.HasCheckConstraint(
                "ck_suppliers_activated_by_second_person",
                "activated_by IS NULL OR (submitted_by IS NOT NULL AND activated_by <> submitted_by)");
            table.HasCheckConstraint(
                "ck_suppliers_activated_with_verified_account",
                "status NOT IN ('Active', 'Blocked') OR account_version > 0");
            table.HasCheckConstraint("ck_suppliers_block_reason", "(status = 'Blocked') = (block_reason IS NOT NULL)");
        });

        builder.HasKey(static supplier => supplier.Id).HasName(SupplierIndexes.SupplierKey);
        builder.Property(static supplier => supplier.Id).ValueGeneratedNever();

        builder.Property(static supplier => supplier.LegalName).HasMaxLength(SupplierDetails.LegalNameMaxLength);
        builder.Property(static supplier => supplier.TaxId)
            .HasConversion(static taxId => taxId.Value, static value => TaxId.Parse(value))
            .HasMaxLength(TaxId.MaxLength);
        builder.Property(static supplier => supplier.Country)
            .HasConversion(static country => country.Value, static value => CountryCode.Parse(value))
            .HasMaxLength(2);
        builder.Property(static supplier => supplier.ContactEmail).HasMaxLength(SupplierDetails.ContactEmailMaxLength);
        builder.Property(static supplier => supplier.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(static supplier => supplier.BlockReason).HasMaxLength(Supplier.ReasonMaxLength);
        builder.Property<uint>(Xmin.Column).IsRowVersion();

        builder.Ignore(static supplier => supplier.VerifiedAccount);
        builder.Ignore(static supplier => supplier.PendingAccount);

        // A deleted supplier would take the bank account history with it. Nothing deletes one, and the database
        // should refuse if something tries.
        builder.HasMany(static supplier => supplier.BankAccounts)
            .WithOne()
            .HasForeignKey(static account => account.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(static supplier => supplier.TaxId).IsUnique().HasDatabaseName(SupplierIndexes.TaxId);

        // The approver's queue: suppliers pending activation, in keyset order.
        builder.HasIndex(static supplier => new { supplier.Status, supplier.Id });
    }
}
