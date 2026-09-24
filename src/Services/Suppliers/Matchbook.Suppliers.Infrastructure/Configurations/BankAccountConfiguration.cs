using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Suppliers.Infrastructure.Configurations;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        // No check on the IBAN column: it will hold ciphertext once it is encrypted at rest.
        builder.ToTable("bank_accounts", static table =>
        {
            table.HasCheckConstraint("ck_bank_accounts_status", "status IN ('Pending', 'Approved', 'Rejected')");
            table.HasCheckConstraint("ck_bank_accounts_bic", "bic ~ '^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$'");
            table.HasCheckConstraint(
                "ck_bank_accounts_approved_by_second_person",
                "status <> 'Approved' OR (decided_by IS NOT NULL AND decided_by <> proposed_by)");
            table.HasCheckConstraint(
                "ck_bank_accounts_decision",
                "(status = 'Pending') = (decided_by IS NULL) AND (decided_by IS NULL) = (decided_at IS NULL)");
            table.HasCheckConstraint(
                "ck_bank_accounts_version_when_approved", "(status = 'Approved') = (account_version IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_bank_accounts_reason_when_rejected", "(status = 'Rejected') = (rejection_reason IS NOT NULL)");
        });

        builder.HasKey(static account => account.Id);

        // Client-generated. Without this EF treats a new account added to a loaded supplier as an existing row
        // and issues an UPDATE that matches nothing.
        builder.Property(static account => account.Id).ValueGeneratedNever();

        builder.Property(static account => account.Iban).HasConversion(new IbanConverter());
        builder.Property(static account => account.Bic)
            .HasConversion(static bic => bic.Value, static value => Bic.Parse(value))
            .HasMaxLength(11);
        builder.Property(static account => account.AccountHolder).HasMaxLength(BankAccount.AccountHolderMaxLength);
        builder.Property(static account => account.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(static account => account.RejectionReason).HasMaxLength(Supplier.ReasonMaxLength);

        // Approving and rejecting the same proposal at once both write this row; the token makes one of them lose.
        builder.Property<uint>(Xmin.Column).IsRowVersion();

        // Two proposals submitted at once both pass the domain's in-memory check; this index lets only one in.
        // Named, like every unique index here, so a violation can be told apart and reported as its rule.
        builder.HasIndex(static account => account.SupplierId, "one_pending_per_supplier")
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_bank_accounts_one_pending_per_supplier");

        // Also the index for loading a supplier's history, so the foreign key needs no index of its own.
        builder.HasIndex(static account => new { account.SupplierId, account.AccountVersion })
            .IsUnique()
            .HasDatabaseName("ux_bank_accounts_account_version");
    }
}
