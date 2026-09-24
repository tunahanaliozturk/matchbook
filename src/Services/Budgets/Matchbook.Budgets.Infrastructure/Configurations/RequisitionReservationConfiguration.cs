using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal sealed class RequisitionReservationConfiguration : IEntityTypeConfiguration<RequisitionReservation>
{
    public void Configure(EntityTypeBuilder<RequisitionReservation> entity)
    {
        entity.ToTable("requisition_reservations", table =>
        {
            table.HasCheckConstraint("ck_requisition_reservations_amount_not_negative", "amount >= 0");
            table.HasCheckConstraint(
                "ck_requisition_reservations_status",
                "status IN ('Held', 'Refused', 'Released', 'Committed')");
            table.HasCheckConstraint(
                "ck_requisition_reservations_held_has_budget",
                "status <> 'Held' OR budget_id IS NOT NULL");
        });

        // The requisition's id is the key, so two messages about the same requisition racing to create its row
        // collide here, and the loser retries against the winner's row.
        entity.HasKey(r => r.RequisitionId);
        entity.Property(r => r.RequisitionId).ValueGeneratedNever();
        entity.Property(r => r.Amount).IsMoney();
        entity.Property(r => r.Status).IsName();
        entity.Property(r => r.Refusal).IsName();
        entity.HasOne<Budget>().WithMany().HasForeignKey(r => r.BudgetId).OnDelete(DeleteBehavior.Restrict);
        entity.HasXminToken();
    }
}
