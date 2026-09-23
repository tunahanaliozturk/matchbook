using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Purchasing.Infrastructure.Persistence;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        string statuses = string.Join(", ", Enum.GetNames<PurchaseOrderStatus>().Select(static name => $"'{name}'"));

        builder.ToTable("purchase_orders", table =>
        {
            table.HasCheckConstraint("ck_purchase_orders_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_purchase_orders_amount", "amount >= 0");
            table.HasCheckConstraint("ck_purchase_orders_commitment_attempt", "commitment_attempt >= 0");
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.Number).HasMaxLength(32);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(order => order.Amount).HasPrecision(18, 2);
        builder.Ignore(order => order.IsClosed);

        // Postgres' own row version. Mapped as a shadow property because nothing in the domain reads it; EF
        // puts it in the WHERE of every UPDATE of the order row, and a lost race surfaces as a concurrency
        // exception rather than a silent overwrite.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        builder.HasIndex(order => order.Number).IsUnique();

        // One order per requisition: the natural key that makes a redelivered RequisitionApproved harmless.
        builder.HasIndex(order => order.RequisitionId).IsUnique();

        // The list endpoint filters by status and pages newest first by id.
        builder.HasIndex(order => new { order.Status, order.Id });

        builder.OwnsMany(order => order.Lines, line =>
        {
            line.ToTable("purchase_order_lines", table =>
            {
                table.HasCheckConstraint("ck_purchase_order_lines_line_number", "line_number > 0");
                table.HasCheckConstraint("ck_purchase_order_lines_quantity", "quantity >= 0");
                table.HasCheckConstraint("ck_purchase_order_lines_unit_price", "unit_price >= 0");
                table.HasCheckConstraint(
                    "ck_purchase_order_lines_received_within_ordered",
                    "received_quantity >= 0 AND received_quantity <= quantity");
                table.HasCheckConstraint(
                    "ck_purchase_order_lines_invoiced_within_received",
                    "invoiced_quantity >= 0 AND invoiced_quantity <= received_quantity");
            });

            line.WithOwner().HasForeignKey("PurchaseOrderId");
            line.HasKey("PurchaseOrderId", nameof(OrderLine.LineNumber));
            line.Property(l => l.LineNumber).ValueGeneratedNever();
            line.Property(l => l.Quantity).HasPrecision(18, 3);
            line.Property(l => l.UnitPrice).HasPrecision(18, 4);
            line.Property(l => l.Amount).HasPrecision(18, 2);
            line.Property(l => l.ReceivedQuantity).HasPrecision(18, 3);
            line.Property(l => l.InvoicedQuantity).HasPrecision(18, 3);
            line.Ignore(l => l.OpenQuantity);
            line.Ignore(l => l.IsSettled);
        });
    }
}
