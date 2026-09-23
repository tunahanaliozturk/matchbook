using Matchbook.Payables.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        // Keyed by Purchasing's receipt id, so a redelivered receipt cannot be counted twice. No foreign key to the
        // order: a receipt can arrive first.
        builder.ToTable("receipts");
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();
        builder.HasIndex(receipt => receipt.PurchaseOrderId);

        builder.OwnsMany(receipt => receipt.Lines, lines =>
        {
            lines.ToTable("receipt_lines", table => table.HasCheckConstraint("ck_receipt_lines_quantity_positive", "quantity > 0"));
            lines.WithOwner().HasForeignKey("ReceiptId");
            lines.HasKey("ReceiptId", nameof(ReceivedLine.LineNumber));
            lines.Property(line => line.LineNumber).ValueGeneratedNever();
            lines.Property(line => line.Quantity).HasPrecision(18, 3);
        });
    }
}
