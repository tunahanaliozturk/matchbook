using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal sealed class RequisitionConfiguration : IEntityTypeConfiguration<Requisition>
{
    // Status is stored as its name, so the constraints below read the way the domain does.
    private const int StatusLength = 20;

    public void Configure(EntityTypeBuilder<Requisition> builder)
    {
        builder.ToTable("requisitions", table =>
        {
            string statuses = string.Join(", ", Enum.GetNames<RequisitionStatus>().Select(static name => $"'{name}'"));
            table.HasCheckConstraint("ck_requisitions_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_requisitions_amount", "amount >= 0");
            // A draft cancelled before submission never reached Budgets and has no fiscal year either.
            table.HasCheckConstraint(
                "ck_requisitions_fiscal_year",
                "fiscal_year IS NOT NULL OR status IN ('Draft', 'Cancelled')");
            table.HasCheckConstraint(
                "ck_requisitions_current_step",
                "(status = 'PendingApproval') = (current_step IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_requisitions_rejection_reason",
                "status NOT IN ('Rejected', 'BudgetRejected') OR rejection_reason IS NOT NULL");
            table.HasCheckConstraint(
                "ck_requisitions_purchase_order",
                "status <> 'Ordered' OR purchase_order_number IS NOT NULL");
        });

        builder.HasKey(static requisition => requisition.Id);
        builder.Property(static requisition => requisition.Id).ValueGeneratedNever();
        builder.UseXminAsRowVersion();

        builder.Property(static requisition => requisition.Number).HasMaxLength(Requisition.MaxNumberLength);
        builder.HasIndex(static requisition => requisition.Number).IsUnique();
        builder.HasIndex(static requisition => requisition.Serial).IsUnique();

        // "My requisitions" pages on (requester, serial); "my approvals" scans only what is pending.
        builder.HasIndex(static requisition => new { requisition.RequesterId, requisition.Serial });
        builder.HasIndex(static requisition => requisition.Serial, "PendingApproval")
            .HasDatabaseName("ix_requisitions_pending_approval")
            .HasFilter("status = 'PendingApproval'");

        builder.Property(static requisition => requisition.CostCentreCode).HasMaxLength(CostCentre.MaxCodeLength);
        builder.Property(static requisition => requisition.Justification).HasMaxLength(Requisition.MaxJustificationLength);
        builder.Property(static requisition => requisition.Amount).HasPrecision(18, Amounts.AmountScale);
        builder.Property(static requisition => requisition.Status).HasConversion<string>().HasMaxLength(StatusLength);

        builder.HasMany(static requisition => requisition.Lines)
            .WithOne()
            .HasForeignKey("RequisitionId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(static requisition => requisition.Steps)
            .WithOne()
            .HasForeignKey("RequisitionId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(static requisition => requisition.Timeline)
            .WithOne()
            .HasForeignKey("RequisitionId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
