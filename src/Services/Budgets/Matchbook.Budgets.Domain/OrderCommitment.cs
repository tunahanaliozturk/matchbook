using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Domain;

public enum CommitmentStatus
{
    /// <summary>Not committed yet: no request decided, or the last attempt was refused.</summary>
    Uncommitted,

    Committed,

    /// <summary>The order is finished with. If it was never committed, this is a tombstone for late requests.</summary>
    Closed,
}

/// <summary>How an earlier decision answers a commitment request, when it does.</summary>
public enum CommitmentReplay
{
    /// <summary>The order is committed; the request is answered with <c>FundsCommitted</c> again.</summary>
    AlreadyCommitted,

    /// <summary>The order was closed before it was ever committed; the request is refused.</summary>
    Closed,

    /// <summary>An attempt no newer than one already decided. Purchasing has moved past it; nothing is sent.</summary>
    Stale,
}

/// <summary>
/// What Budgets knows about one purchase order's funds: whether and where it was committed, and how much of
/// that commitment invoices and closing have not yet taken away.
/// </summary>
public sealed class OrderCommitment
{
    private OrderCommitment(
        Guid purchaseOrderId,
        Guid requisitionId,
        Guid? budgetId,
        int lastAttempt,
        decimal amount,
        decimal remaining,
        CommitmentStatus status,
        FundsRefusal? refusal)
    {
        PurchaseOrderId = purchaseOrderId;
        RequisitionId = requisitionId;
        BudgetId = budgetId;
        LastAttempt = lastAttempt;
        Amount = amount;
        Remaining = remaining;
        Status = status;
        Refusal = refusal;
    }

    public Guid PurchaseOrderId { get; private set; }

    public Guid RequisitionId { get; private set; }

    /// <summary>Set once the order is committed, and never cleared: invoices after closing still land here.</summary>
    public Guid? BudgetId { get; private set; }

    /// <summary>The highest commitment attempt decided so far.</summary>
    public int LastAttempt { get; private set; }

    /// <summary>The committed amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The part of the commitment that neither invoices nor closing have taken away yet.</summary>
    public decimal Remaining { get; private set; }

    public CommitmentStatus Status { get; private set; }

    /// <summary>Why the last attempt was refused, while the order is uncommitted.</summary>
    public FundsRefusal? Refusal { get; private set; }

    public bool WasCommitted => BudgetId is not null;

    public static OrderCommitment Track(Guid purchaseOrderId, Guid requisitionId) =>
        new(purchaseOrderId, requisitionId, budgetId: null, lastAttempt: 0, 0m, 0m, CommitmentStatus.Uncommitted, refusal: null);

    public static BusinessRuleException NeverCommitted(Guid purchaseOrderId) =>
        new(
            "funds.order_not_committed",
            $"Purchase order {purchaseOrderId} was never committed, so an invoice against it has no budget to land on.",
            ViolationKind.NotFound);

    /// <summary>
    /// How an earlier decision answers a request for <paramref name="attempt"/>, or null when the request is a new
    /// attempt that has to be decided.
    /// </summary>
    public CommitmentReplay? Replay(int attempt)
    {
        if (attempt < 1)
        {
            throw new BusinessRuleException(
                "funds.attempt_invalid", $"Commitment attempts count from 1, not {attempt}.", ViolationKind.Invalid);
        }

        return WasCommitted ? CommitmentReplay.AlreadyCommitted
            : Status == CommitmentStatus.Closed ? CommitmentReplay.Closed
            : attempt <= LastAttempt ? CommitmentReplay.Stale
            : null;
    }

    public void Commit(Guid budgetId, int attempt, decimal amount)
    {
        Money.Positive(amount, "The order amount");
        Decide(attempt);
        BudgetId = budgetId;
        Amount = amount;
        Remaining = amount;
        Status = CommitmentStatus.Committed;
        Refusal = null;
    }

    public void Refuse(int attempt, FundsRefusal reason)
    {
        Decide(attempt);
        Refusal = reason;
    }

    /// <summary>
    /// Moves a matched invoice from commitment to actual. The commitment gives up at most what it still holds,
    /// min(amount, remaining), and actual takes the whole invoice. That is why the order of this and
    /// <see cref="Close"/> does not matter: either way the commitment ends at zero and actual at the invoices'
    /// sum.
    /// </summary>
    public LedgerEntry Invoice(Guid invoiceId, decimal amount, DateTimeOffset occurredAt, DateTimeOffset now)
    {
        if (BudgetId is not { } budgetId)
        {
            throw NeverCommitted(PurchaseOrderId);
        }

        Money.Positive(amount, "The invoice amount");
        decimal relief = Math.Min(amount, Remaining);
        Remaining -= relief;
        return LedgerEntry.Record(budgetId, invoiceId, LedgerStep.Invoice, Movement.Invoice(relief, amount), occurredAt, now);
    }

    /// <summary>
    /// Finishes the order and returns the entry that releases what is still committed, or null when there is
    /// nothing to release. An order closed before it was committed releases nothing here; its requisition's
    /// reservation is the caller's to release.
    /// </summary>
    public LedgerEntry? Close(DateTimeOffset occurredAt, DateTimeOffset now)
    {
        if (Status == CommitmentStatus.Closed)
        {
            return null;
        }

        Status = CommitmentStatus.Closed;
        if (BudgetId is not { } budgetId || Remaining == 0m)
        {
            return null;
        }

        decimal released = Remaining;
        Remaining = 0m;
        return LedgerEntry.Record(
            budgetId, PurchaseOrderId, LedgerStep.Close, Movement.ReleaseCommitment(released), occurredAt, now);
    }

    private void Decide(int attempt)
    {
        if (Replay(attempt) is { } replay)
        {
            throw new InvalidOperationException(
                $"Attempt {attempt} for purchase order {PurchaseOrderId} was already answered ({replay}).");
        }

        LastAttempt = attempt;
    }
}
