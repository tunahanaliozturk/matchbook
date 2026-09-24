namespace Matchbook.Budgets.Domain;

/// <summary>What a ledger entry records. Together with the document it is the entry's natural key.</summary>
public enum LedgerStep
{
    /// <summary>The budget was opened with its first allotment. The document is the budget.</summary>
    Open,

    /// <summary>A budget admin raised or lowered the allotment. The document is the change.</summary>
    Allot,

    /// <summary>Funds were set aside for a requisition.</summary>
    Reserve,

    /// <summary>A requisition's reservation was given back.</summary>
    Release,

    /// <summary>A purchase order took over its requisition's reservation and committed its own amount.</summary>
    Commit,

    /// <summary>A matched invoice moved commitment to actual. The document is the invoice.</summary>
    Invoice,

    /// <summary>A closed order released what was still committed.</summary>
    Close,
}

/// <summary>
/// One line of a budget's append-only ledger. An entry is unique by document and step, which is what makes a
/// redelivered message harmless: the second insert of the same key cannot happen.
/// </summary>
public sealed class LedgerEntry
{
    private LedgerEntry(
        Guid budgetId,
        Guid documentId,
        LedgerStep step,
        decimal allotted,
        decimal reserved,
        decimal committed,
        decimal actual,
        Guid? actorId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        BudgetId = budgetId;
        DocumentId = documentId;
        Step = step;
        Allotted = allotted;
        Reserved = reserved;
        Committed = committed;
        Actual = actual;
        ActorId = actorId;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
    }

    /// <summary>Assigned by the database. Within one budget it follows commit order (see the service notes).</summary>
    public long Sequence { get; private set; }

    public Guid BudgetId { get; private set; }

    public Guid DocumentId { get; private set; }

    public LedgerStep Step { get; private set; }

    public decimal Allotted { get; private set; }

    public decimal Reserved { get; private set; }

    public decimal Committed { get; private set; }

    public decimal Actual { get; private set; }

    /// <summary>The budget admin behind an opening or an allotment change; empty for entries a message caused.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>When the business event happened, as its publisher stated it.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Movement Movement => new(Allotted, Reserved, Committed, Actual);

    // Postgres stores timestamptz as UTC and Npgsql refuses a non-zero offset, so it is normalised here once.
    public static LedgerEntry Record(
        Guid budgetId,
        Guid documentId,
        LedgerStep step,
        Movement movement,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        Guid? actorId = null) =>
        new(
            budgetId,
            documentId,
            step,
            movement.Allotted,
            movement.Reserved,
            movement.Committed,
            movement.Actual,
            actorId,
            occurredAt.ToUniversalTime(),
            now.ToUniversalTime());
}
