using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> entity)
    {
        // The figures that always hold. Available itself may go negative (an overspend is allowed and reported),
        // so the floor that grants respect is enforced by their UPDATE, not here. What is enforced here is what no
        // sequence of entries may ever produce: a negative figure, or more reserved and committed than allotted.
        entity.ToTable("budgets", table =>
        {
            table.HasCheckConstraint("ck_budgets_allotted_not_negative", "allotted >= 0");
            table.HasCheckConstraint("ck_budgets_reserved_not_negative", "reserved >= 0");
            table.HasCheckConstraint("ck_budgets_committed_not_negative", "committed >= 0");
            table.HasCheckConstraint("ck_budgets_actual_not_negative", "actual >= 0");
            table.HasCheckConstraint("ck_budgets_reserved_and_committed_within_allotted", "reserved + committed <= allotted");
            table.HasCheckConstraint(
                "ck_budgets_fiscal_year_range",
                $"fiscal_year BETWEEN {Budget.EarliestFiscalYear} AND {Budget.LatestFiscalYear}");
        });

        entity.HasKey(b => b.Id);
        entity.Property(b => b.Id).ValueGeneratedNever();
        entity.Property(b => b.CostCentreCode).HasMaxLength(CostCentre.CodeMaxLength);
        entity.Property(b => b.Allotted).IsMoney();
        entity.Property(b => b.Reserved).IsMoney();
        entity.Property(b => b.Committed).IsMoney();
        entity.Property(b => b.Actual).IsMoney();

        // Year first: it serves the lookup by cost centre and year and the per-year lists in code order.
        entity.HasIndex(b => new { b.FiscalYear, b.CostCentreCode }).IsUnique();
        entity.HasOne<CostCentre>().WithMany().HasForeignKey(b => b.CostCentreCode).OnDelete(DeleteBehavior.Restrict);

        // No concurrency token: every reservation changes this row, so a token would turn each one into a
        // conflict for everyone else. The figures change only by guarded, additive UPDATEs instead.
    }
}
