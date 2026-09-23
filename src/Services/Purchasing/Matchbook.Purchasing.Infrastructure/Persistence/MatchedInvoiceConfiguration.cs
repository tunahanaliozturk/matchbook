using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Purchasing.Infrastructure.Persistence;

internal sealed class MatchedInvoiceConfiguration : IEntityTypeConfiguration<MatchedInvoice>
{
    public void Configure(EntityTypeBuilder<MatchedInvoice> builder)
    {
        builder.ToTable("matched_invoices");

        // The invoice id alone is the key: an invoice is counted once, whichever order it names.
        builder.HasKey(invoice => invoice.InvoiceId);
        builder.Property(invoice => invoice.InvoiceId).ValueGeneratedNever();
        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(invoice => invoice.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
