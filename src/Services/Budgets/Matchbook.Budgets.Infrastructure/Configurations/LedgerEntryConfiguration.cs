using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> entity)
    {
        entity.ToTable("ledger_entries", table =>
            table.HasCheckConstraint(
                "ck_ledger_entries_step",
                "step IN ('Open', 'Allot', 'Reserve', 'Release', 'Commit', 'Invoice', 'Close')"));

        entity.HasKey(e => e.Sequence);
        entity.Property(e => e.Sequence).UseIdentityAlwaysColumn();
        entity.Property(e => e.Step).IsName();
        entity.Property(e => e.Allotted).IsMoney();
        entity.Property(e => e.Reserved).IsMoney();
        entity.Property(e => e.Committed).IsMoney();
        entity.Property(e => e.Actual).IsMoney();

        // The natural key. A redelivered message that slipped past every other check still cannot write its
        // entry twice.
        entity.HasIndex(e => new { e.DocumentId, e.Step }).IsUnique();

        // Serves the ledger listing: one budget, in sequence order, from a cursor.
        entity.HasIndex(e => new { e.BudgetId, e.Sequence });
        entity.HasOne<Budget>().WithMany().HasForeignKey(e => e.BudgetId).OnDelete(DeleteBehavior.Restrict);
    }
}
