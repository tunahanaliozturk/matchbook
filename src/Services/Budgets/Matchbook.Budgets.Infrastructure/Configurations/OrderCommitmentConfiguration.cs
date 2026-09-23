using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal sealed class OrderCommitmentConfiguration : IEntityTypeConfiguration<OrderCommitment>
{
    public void Configure(EntityTypeBuilder<OrderCommitment> entity)
    {
        entity.ToTable("order_commitments", table =>
        {
            table.HasCheckConstraint("ck_order_commitments_amount_not_negative", "amount >= 0");
            table.HasCheckConstraint("ck_order_commitments_remaining_within_amount", "remaining >= 0 AND remaining <= amount");
            table.HasCheckConstraint("ck_order_commitments_last_attempt_not_negative", "last_attempt >= 0");
            table.HasCheckConstraint(
                "ck_order_commitments_status",
                "status IN ('Uncommitted', 'Committed', 'Closed')");
            table.HasCheckConstraint(
                "ck_order_commitments_committed_has_budget",
                "status <> 'Committed' OR budget_id IS NOT NULL");
        });

        entity.HasKey(o => o.PurchaseOrderId);
        entity.Property(o => o.PurchaseOrderId).ValueGeneratedNever();
        entity.Property(o => o.Amount).IsMoney();
        entity.Property(o => o.Remaining).IsMoney();
        entity.Property(o => o.Status).IsName();
        entity.Property(o => o.Refusal).IsName();
        entity.HasOne<Budget>().WithMany().HasForeignKey(o => o.BudgetId).OnDelete(DeleteBehavior.Restrict);

        // Two invoices for the same order read the same remaining commitment; the token makes the second one
        // retry against the first one's result instead of relieving the same money twice.
        entity.HasXminToken();
    }
}
