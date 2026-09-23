using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal sealed class RequisitionLineConfiguration : IEntityTypeConfiguration<RequisitionLine>
{
    public void Configure(EntityTypeBuilder<RequisitionLine> builder)
    {
        builder.ToTable("requisition_lines", table =>
        {
            table.HasCheckConstraint("ck_requisition_lines_line_number", $"line_number BETWEEN 1 AND {Requisition.MaxLines}");
            table.HasCheckConstraint("ck_requisition_lines_quantity", "quantity > 0");
            table.HasCheckConstraint("ck_requisition_lines_unit_price", "unit_price >= 0");

            // Postgres rounds numeric half away from zero, as Amounts.Line does, so a line amount computed any
            // other way cannot be stored.
            table.HasCheckConstraint("ck_requisition_lines_amount", "amount = round(quantity * unit_price, 2)");
        });

        builder.HasKey("RequisitionId", nameof(RequisitionLine.LineNumber));
        builder.Property(static line => line.LineNumber).ValueGeneratedNever();

        builder.Property(static line => line.Description).HasMaxLength(RequisitionLine.MaxDescriptionLength);
        builder.Property(static line => line.UnitOfMeasure).HasMaxLength(RequisitionLine.MaxUnitOfMeasureLength);
        builder.Property(static line => line.Quantity).HasPrecision(18, Amounts.QuantityScale);
        builder.Property(static line => line.UnitPrice).HasPrecision(18, Amounts.UnitPriceScale);
        builder.Property(static line => line.Amount).HasPrecision(18, Amounts.AmountScale);
    }
}
