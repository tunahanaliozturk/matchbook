using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class PaymentRunItemConfiguration : IEntityTypeConfiguration<PaymentRunItem>
{
    public const string ActiveInvoiceIndex = "ux_payment_run_items_active_invoice";

    public void Configure(EntityTypeBuilder<PaymentRunItem> builder)
    {
        builder.ToTable("payment_run_items", table => table.HasCheckConstraint("ck_payment_run_items_amount_positive", "amount > 0"));

        builder.HasKey(item => new { item.PaymentRunId, item.InvoiceId });
        builder.Property(item => item.SupplierInvoiceNumber).HasMaxLength(InvoiceNumber.MaxLength);
        builder.Property(item => item.Amount).HasPrecision(18, 2);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(Columns.EnumLength);

        // An invoice is in at most one run that has scheduled or paid it. This is the guarantee behind paying an
        // invoice at most once: two drafts at the same moment cannot both insert it, and a paid item keeps holding it.
        builder.HasIndex(item => item.InvoiceId)
            .IsUnique()
            .HasFilter("status IN ('Scheduled', 'Paid')")
            .HasDatabaseName(ActiveInvoiceIndex);

        // The run's own invoices by supplier, for dropping a supplier at release and for the bank file.
        builder.HasIndex(item => new { item.PaymentRunId, item.Status, item.SupplierId });

        builder.HasOne<PaymentRun>().WithMany().HasForeignKey(item => item.PaymentRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(item => item.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
