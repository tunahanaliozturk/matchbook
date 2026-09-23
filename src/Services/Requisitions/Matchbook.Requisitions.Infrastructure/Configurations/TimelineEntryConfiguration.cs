using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Requisitions.Infrastructure.Configurations;

internal sealed class TimelineEntryConfiguration : IEntityTypeConfiguration<TimelineEntry>
{
    private const int ActionLength = 20;

    public void Configure(EntityTypeBuilder<TimelineEntry> builder)
    {
        builder.ToTable("requisition_timeline", static table =>
            table.HasCheckConstraint("ck_requisition_timeline_sequence", "sequence >= 1"));

        builder.HasKey(static entry => entry.Id);
        builder.Property(static entry => entry.Id).ValueGeneratedNever();

        // Deliberately not unique. Two writers who read the same revision both append the same sequence, and
        // the requisition's xmin already turns the loser into a concurrency conflict. A unique index here could
        // fail first, in the same batch, and report the lost race as a constraint violation instead.
        builder.HasIndex("RequisitionId", nameof(TimelineEntry.Sequence));

        builder.Property(static entry => entry.Action).HasConversion<string>().HasMaxLength(ActionLength);
    }
}
