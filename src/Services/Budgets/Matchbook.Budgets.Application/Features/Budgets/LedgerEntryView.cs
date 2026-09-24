using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.Application.Features.Budgets;

public sealed record LedgerEntryView(
    long Sequence,
    Guid DocumentId,
    string Step,
    decimal Allotted,
    decimal Reserved,
    decimal Committed,
    decimal Actual,
    Guid? ActorId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt)
{
    internal static LedgerEntryView From(LedgerEntry entry) =>
        new(
            entry.Sequence,
            entry.DocumentId,
            entry.Step.ToString(),
            entry.Allotted,
            entry.Reserved,
            entry.Committed,
            entry.Actual,
            entry.ActorId,
            entry.OccurredAt,
            entry.RecordedAt);
}
