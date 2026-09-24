using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    private const int NameLength = 16;

    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("approval_steps", table =>
        {
            table.HasCheckConstraint("ck_approval_steps_sequence", $"sequence BETWEEN 1 AND {ApprovalStep.MaxSteps}");
            table.HasCheckConstraint("ck_approval_steps_manager", "(kind = 'Manager') = (approver_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_approval_steps_decision",
                "(decision = 'Pending') = (decided_by IS NULL) AND (decided_by IS NULL) = (decided_at IS NULL)");
        });

        // The natural key: a route is built once, and a redelivered FundsReserved that got past the status
        // check would collide here rather than add a second route.
        builder.HasKey("RequisitionId", nameof(ApprovalStep.Sequence));
        builder.Property(static step => step.Sequence).ValueGeneratedNever();

        // Separation of duties, held by the database as well: one person decides at most one step of a
        // requisition. Pending steps have no decider, and Postgres treats those NULLs as distinct.
        builder.HasIndex("RequisitionId", nameof(ApprovalStep.DecidedBy)).IsUnique();

        // A manager's own queue: the steps waiting on them by name.
        builder.HasIndex(static step => step.ApproverId).HasFilter("decision = 'Pending'");

        builder.Property(static step => step.Kind).HasConversion<string>().HasMaxLength(NameLength);
        builder.Property(static step => step.Decision).HasConversion<string>().HasMaxLength(NameLength);
    }
}
