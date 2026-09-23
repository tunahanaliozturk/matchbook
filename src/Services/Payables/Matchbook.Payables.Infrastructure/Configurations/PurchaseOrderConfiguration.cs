using Matchbook.Payables.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Payables.Infrastructure.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        // The revision is what serialises matches on the order; see PurchaseOrder.
        builder.Property(order => order.Revision).IsConcurrencyToken();

        builder.Property(order => order.Number).HasMaxLength(32);
        builder.Property(order => order.CloseReason).HasMaxLength(Columns.EnumLength);

        builder.OwnsMany(order => order.Lines, lines =>
        {
            lines.ToTable("purchase_order_lines");
            lines.WithOwner().HasForeignKey("PurchaseOrderId");
            lines.HasKey("PurchaseOrderId", nameof(OrderedLine.LineNumber));
            lines.Property(line => line.LineNumber).ValueGeneratedNever();
            lines.Property(line => line.Quantity).HasPrecision(18, 3);
            lines.Property(line => line.UnitPrice).HasPrecision(18, 4);
        });
    }
}
