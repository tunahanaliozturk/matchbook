using Matchbook.Payables.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public const string UniqueNumberIndex = "ux_invoices_supplier_number";

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint("ck_invoices_total_positive", "total > 0");

            // Separation of duties, held by the database as well as the domain.
            table.HasCheckConstraint(
                "ck_invoices_variance_second_person",
                "variance_accepted_by IS NULL OR variance_accepted_by <> captured_by");
            table.HasCheckConstraint(
                "ck_invoices_duplicate_second_person",
                "duplicate_cleared_by IS NULL OR duplicate_cleared_by <> captured_by");
        });

        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();
        builder.Property<uint>(Columns.RowVersion).IsRowVersion();

        builder.Property(invoice => invoice.Number).HasMaxLength(InvoiceNumber.MaxLength);
        builder.Property(invoice => invoice.NormalisedNumber).HasMaxLength(InvoiceNumber.MaxLength);
        builder.Property(invoice => invoice.Total).HasPrecision(18, 2);
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(Columns.EnumLength);
        builder.Property(invoice => invoice.Reason).HasConversion<string>().HasMaxLength(Columns.EnumLength);
        builder.Property(invoice => invoice.ReasonDetail).HasMaxLength(2000);
        builder.Property(invoice => invoice.VarianceAcceptanceReason).HasMaxLength(Invoice.MaxReasonLength);

        // A rejected invoice is no claim, so its number may be captured again, correctly this time.
        builder.HasIndex(invoice => new { invoice.SupplierId, invoice.NormalisedNumber })
            .IsUnique()
            .HasFilter("status <> 'Rejected'")
            .HasDatabaseName(UniqueNumberIndex);

        // Invoices waiting on an order; lists and the exceptions queue by status; payment run candidates; lookalikes.
        builder.HasIndex(invoice => new { invoice.PurchaseOrderId, invoice.Status });
        builder.HasIndex(invoice => new { invoice.Status, invoice.Id });
        builder.HasIndex(invoice => new { invoice.Status, invoice.DueDate });
        builder.HasIndex(invoice => new { invoice.SupplierId, invoice.Total });

        builder.OwnsMany(invoice => invoice.Lines, lines =>
        {
            lines.ToTable("invoice_lines", table =>
            {
                table.HasCheckConstraint("ck_invoice_lines_quantity_positive", "quantity > 0");
                table.HasCheckConstraint("ck_invoice_lines_unit_price_not_negative", "unit_price >= 0");
            });
            lines.WithOwner().HasForeignKey("InvoiceId");
            lines.HasKey("InvoiceId", nameof(InvoiceLine.LineNumber));
            lines.Property(line => line.LineNumber).ValueGeneratedNever();
            lines.Property(line => line.Quantity).HasPrecision(18, 3);
            lines.Property(line => line.UnitPrice).HasPrecision(18, 4);
        });
    }
}
