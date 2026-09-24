using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Purchasing.Infrastructure.Persistence;

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts");
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();
        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(receipt => receipt.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(receipt => receipt.Lines, line =>
        {
            line.ToTable("goods_receipt_lines", table =>
                table.HasCheckConstraint("ck_goods_receipt_lines_quantity", "quantity > 0"));

            line.WithOwner().HasForeignKey("GoodsReceiptId");
            line.HasKey("GoodsReceiptId", nameof(GoodsReceiptLine.LineNumber));
            line.Property(l => l.LineNumber).ValueGeneratedNever();
            line.Property(l => l.Quantity).HasPrecision(18, 3);
        });
    }
}
