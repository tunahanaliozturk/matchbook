using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain.PaymentRuns;

/// <summary>
/// A batch of payable invoices to be paid on one execution date. One treasurer drafts it, a different one releases
/// it, and release is when money is committed: it pays what can still be paid and produces the bank file.
/// </summary>
/// <remarks>
/// The run holds one <see cref="PaymentRunCreditor"/> per supplier, and the decisions at release are made per
/// supplier, because the rule is about the supplier: still active, and still on the bank account the run was drafted
/// against. The per-invoice <see cref="PaymentRunItem"/>s follow their creditor, which is what lets a run of a
/// hundred thousand invoices be released with a few set-based statements instead of one update per invoice.
/// </remarks>
public sealed class PaymentRun
{
    private readonly List<PaymentRunCreditor> _creditors = [];

    private PaymentRun(
        Guid id,
        DateOnly executionDate,
        PaymentRunStatus status,
        Guid draftedBy,
        DateTimeOffset draftedAt,
        int itemCount,
        decimal total)
    {
        Id = id;
        ExecutionDate = executionDate;
        Status = status;
        DraftedBy = draftedBy;
        DraftedAt = draftedAt;
        ItemCount = itemCount;
        Total = total;
    }

    public Guid Id { get; private set; }

    public DateOnly ExecutionDate { get; private set; }

    public PaymentRunStatus Status { get; private set; }

    public Guid DraftedBy { get; private set; }

    public DateTimeOffset DraftedAt { get; private set; }

    public Guid? ReleasedBy { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Invoices taken into the draft.</summary>
    public int ItemCount { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Invoices paid at release: the count and control sum of the bank file.</summary>
    public int PaidCount { get; private set; }

    public decimal PaidTotal { get; private set; }

    public IReadOnlyList<PaymentRunCreditor> Creditors => _creditors;

    /// <summary>
    /// Takes every candidate that is payable, due by <paramref name="executionDate"/>, and owed to a supplier that is
    /// active with a verified account, each invoice once. An invoice without a due date (matched before its supplier
    /// arrived) is due on its invoice date plus the terms the supplier has now.
    /// </summary>
    public static PaymentRunDraft Draft(
        Guid id,
        DateOnly executionDate,
        Actor treasurer,
        IEnumerable<PaymentCandidate> candidates,
        IReadOnlyDictionary<Guid, Supplier> suppliers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(treasurer);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(suppliers);

        EnsureTreasurer(treasurer);
        if (executionDate < Today(now))
        {
            throw new BusinessRuleException(
                "payment_run.execution_date_past",
                "A payment run cannot be drafted for a date that has passed.",
                ViolationKind.Invalid);
        }

        var items = new List<PaymentRunItem>();
        foreach (PaymentCandidate candidate in candidates.DistinctBy(candidate => candidate.InvoiceId))
        {
            if (candidate.Status != InvoiceStatus.Payable
                || !suppliers.TryGetValue(candidate.SupplierId, out Supplier? supplier)
                || !supplier.CanBePaid)
            {
                continue;
            }

            DateOnly due = candidate.DueDate ?? candidate.InvoiceDate.AddDays(supplier.PaymentTermsDays);
            if (due <= executionDate)
            {
                items.Add(PaymentRunItem.Schedule(id, candidate));
            }
        }

        if (items.Count == 0)
        {
            throw new BusinessRuleException(
                "payment_run.nothing_due",
                "No payable invoice is due by that date for a supplier that can be paid.",
                ViolationKind.Conflict);
        }

        var run = new PaymentRun(
            id,
            executionDate,
            PaymentRunStatus.Draft,
            treasurer.Id,
            now,
            items.Count,
            items.Sum(item => item.Amount));
        run._creditors.AddRange(
            items.GroupBy(item => item.SupplierId)
                .OrderBy(group => group.Key)
                .Select(group => PaymentRunCreditor.Schedule(
                    suppliers[group.Key],
                    group.Count(),
                    group.Sum(item => item.Amount))));

        return new PaymentRunDraft(run, items);
    }

    /// <summary>
    /// Releases the run. A supplier that is no longer active, or whose bank account changed since the draft, is
    /// dropped with its invoices, which stay payable; everyone else is paid.
    /// </summary>
    public void Release(Actor treasurer, IReadOnlyDictionary<Guid, Supplier> suppliers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(treasurer);
        ArgumentNullException.ThrowIfNull(suppliers);

        EnsureTreasurer(treasurer);
        EnsureDraft();
        if (treasurer.Id == DraftedBy)
        {
            throw new BusinessRuleException(
                "payment_run.same_treasurer",
                "The treasurer who drafted a payment run cannot release it.",
                ViolationKind.Forbidden);
        }

        if (ExecutionDate < Today(now))
        {
            throw new BusinessRuleException(
                "payment_run.execution_date_passed",
                "The execution date has passed. Cancel the run and draft a new one.",
                ViolationKind.Conflict);
        }

        (PaymentRunCreditor Creditor, CreditorDropReason? Drop)[] decisions =
            [.. _creditors.Select(creditor => (creditor, creditor.DropReasonAgainst(suppliers.GetValueOrDefault(creditor.SupplierId))))];
        if (decisions.All(decision => decision.Drop is not null))
        {
            throw new BusinessRuleException(
                "payment_run.nothing_payable",
                "Every supplier in the run is blocked or has a new bank account. Cancel the run instead.",
                ViolationKind.Conflict);
        }

        foreach ((PaymentRunCreditor creditor, CreditorDropReason? drop) in decisions)
        {
            if (drop is { } reason)
            {
                creditor.Drop(reason);
            }
            else
            {
                creditor.Pay();
            }
        }

        Status = PaymentRunStatus.Released;
        ReleasedBy = treasurer.Id;
        ReleasedAt = now;
        PaidCount = _creditors.Where(creditor => creditor.Status == CreditorStatus.Paid).Sum(creditor => creditor.ItemCount);
        PaidTotal = _creditors.Where(creditor => creditor.Status == CreditorStatus.Paid).Sum(creditor => creditor.Total);
    }

    /// <summary>Cancels a draft. Its invoices become payable again.</summary>
    public void Cancel(Actor treasurer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(treasurer);

        EnsureTreasurer(treasurer);
        EnsureDraft();

        Status = PaymentRunStatus.Cancelled;
        CancelledBy = treasurer.Id;
        CancelledAt = now;
    }

    private void EnsureDraft()
    {
        if (Status != PaymentRunStatus.Draft)
        {
            throw new BusinessRuleException(
                "payment_run.not_draft",
                $"The payment run is {Status}; only a draft can be released or cancelled.",
                ViolationKind.Conflict);
        }
    }

    private static void EnsureTreasurer(Actor actor)
    {
        if (!actor.IsIn(Roles.Treasurer))
        {
            throw new BusinessRuleException(
                "payment_run.treasurer_required",
                "Only a treasurer may do this.",
                ViolationKind.Forbidden);
        }
    }

    // Execution dates are calendar days in UTC, like every other date in the system.
    private static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);
}

public enum PaymentRunStatus
{
    Draft,
    Released,
    Cancelled,
}

/// <summary>A drafted run and the invoices it took, which are stored beside it rather than inside it.</summary>
public sealed record PaymentRunDraft(PaymentRun Run, IReadOnlyList<PaymentRunItem> Items);
