using Matchbook.Payables.Domain.PaymentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class PaymentRunConfiguration : IEntityTypeConfiguration<PaymentRun>
{
    public void Configure(EntityTypeBuilder<PaymentRun> builder)
    {
        builder.ToTable("payment_runs", table =>
            table.HasCheckConstraint("ck_payment_runs_released_by_second_treasurer", "released_by IS NULL OR released_by <> drafted_by"));

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();

        // Release and cancel both start by writing the run under its row version, so only one of them, once, wins.
        builder.Property<uint>(Columns.RowVersion).IsRowVersion();

        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(Columns.EnumLength);
        builder.Property(run => run.Total).HasPrecision(18, 2);
        builder.Property(run => run.PaidTotal).HasPrecision(18, 2);

        builder.OwnsMany(run => run.Creditors, creditors =>
        {
            creditors.ToTable("payment_run_creditors", table => table.HasCheckConstraint(
                "ck_payment_run_creditors_iban_protected",
                $"protected_iban LIKE '{BankColumns.ProtectedPrefix}%'"));
            creditors.WithOwner().HasForeignKey("PaymentRunId");
            creditors.HasKey("PaymentRunId", nameof(PaymentRunCreditor.SupplierId));
            creditors.Property(creditor => creditor.SupplierId).ValueGeneratedNever();
            creditors.Property(creditor => creditor.AccountHolder).HasMaxLength(200);
            creditors.Property(creditor => creditor.IbanLastFour).HasMaxLength(4);
            creditors.Property(creditor => creditor.Bic).IsBic();
            creditors.Property(creditor => creditor.Total).HasPrecision(18, 2);
            creditors.Property(creditor => creditor.Status).HasConversion<string>().HasMaxLength(Columns.EnumLength);
            creditors.Property(creditor => creditor.DropReason).HasConversion<string>().HasMaxLength(Columns.EnumLength);
        });
    }
}
