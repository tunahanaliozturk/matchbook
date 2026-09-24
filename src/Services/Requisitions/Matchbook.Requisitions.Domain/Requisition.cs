using System.Globalization;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Domain;

/// <summary>
/// A request to buy from one supplier against one cost centre, and the approval route it has to pass before
/// Purchasing may order it.
/// </summary>
/// <remarks>
/// Every change raises <see cref="Revision"/> and appends one timeline entry numbered by it. That keeps the
/// timeline in order without reading it back, and it means every change writes the requisition's own row, so
/// the row's concurrency token is checked even when a change only touches an approval step.
/// </remarks>
public sealed class Requisition
{
    public const int MaxLines = 50;
    public const int MaxJustificationLength = 2000;
    public const int MaxReasonLength = 1000;
    public const int MaxNumberLength = 32;

    /// <summary>The largest value a numeric(18,2) column holds.</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private readonly List<RequisitionLine> _lines = [];
    private readonly List<ApprovalStep> _steps = [];
    private readonly List<TimelineEntry> _timeline = [];

    private Requisition(
        Guid id,
        string number,
        long serial,
        Guid requesterId,
        string requesterName,
        string costCentreCode,
        Guid supplierId,
        string justification,
        DateOnly neededBy,
        DateTimeOffset createdAt)
    {
        Id = id;
        Number = number;
        Serial = serial;
        RequesterId = requesterId;
        RequesterName = requesterName;
        CostCentreCode = costCentreCode;
        SupplierId = supplierId;
        Justification = justification;
        NeededBy = neededBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>The human-facing number, <c>REQ-2026-000042</c>.</summary>
    public string Number { get; private set; }

    /// <summary>The sequence value behind <see cref="Number"/>. Unique and rising, so lists page on it.</summary>
    public long Serial { get; private set; }

    public Guid RequesterId { get; private set; }

    public string RequesterName { get; private set; }

    public string CostCentreCode { get; private set; }

    public Guid SupplierId { get; private set; }

    public string Justification { get; private set; }

    public DateOnly NeededBy { get; private set; }

    /// <summary>The sum of the line amounts.</summary>
    public decimal Amount { get; private set; }

    public RequisitionStatus Status { get; private set; }

    /// <summary>The fiscal year Budgets reserves in: the UTC calendar year of submission. Null while a draft.</summary>
    public int? FiscalYear { get; private set; }

    /// <summary>The sequence of the step waiting for a decision. Set only while pending approval.</summary>
    public int? CurrentStep { get; private set; }

    /// <summary>The approver's reason, or Budgets' reason code when the reservation was refused.</summary>
    public string? RejectionReason { get; private set; }

    public Guid? PurchaseOrderId { get; private set; }

    public string? PurchaseOrderNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int Revision { get; private set; }

    public IReadOnlyList<RequisitionLine> Lines => _lines;

    public IReadOnlyList<ApprovalStep> Steps => _steps;

    public IReadOnlyList<TimelineEntry> Timeline => _timeline;

    /// <param name="id">The client's own id when it sent one, so that a retried create finds the first.</param>
    public static Requisition Draft(Guid id, long serial, Actor requester, RequisitionDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serial);

        Checked valid = Validate(details, now);
        string number = string.Create(CultureInfo.InvariantCulture, $"REQ-{now.UtcDateTime.Year}-{serial:D6}");

        Requisition requisition = new(
            id,
            number,
            serial,
            requester.Id,
            requester.Name,
            valid.CostCentreCode,
            details.SupplierId,
            valid.Justification,
            details.NeededBy,
            now);

        requisition._lines.AddRange(valid.Lines);
        requisition.Amount = valid.Amount;
        requisition.Record(TimelineAction.Created, now, requester);
        return requisition;
    }

    /// <summary>
    /// Anyone who approves, and the auditor, read every requisition. Everyone else reads only their own and is
    /// told the others do not exist.
    /// </summary>
    public static bool SeesEveryRequisition(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.IsIn(Roles.Approver)
            || actor.IsIn(Roles.FinanceApprover)
            || actor.IsIn(Roles.Cfo)
            || actor.IsIn(Roles.Auditor);
    }

    public bool IsVisibleTo(Actor actor) => SeesEveryRequisition(actor) || actor.Id == RequesterId;

    /// <summary>
    /// Whether a create request from <paramref name="requester"/> with <paramref name="details"/> asks for this
    /// requisition as it stands, read the way <see cref="Draft"/> reads it. A retried create gets the first
    /// one's result only when it does.
    /// </summary>
    public bool IsRepeatOf(Actor requester, RequisitionDetails details)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(details);

        return requester.Id == RequesterId
            && NormaliseCode(details.CostCentreCode) == CostCentreCode
            && details.SupplierId == SupplierId
            && details.Justification?.Trim() == Justification
            && details.NeededBy == NeededBy
            && details.Lines is { } lines
            && lines.Count == _lines.Count
            && lines.Select(static (input, index) => (input, number: index + 1))
                .All(pair => _lines.Single(line => line.LineNumber == pair.number).Matches(pair.input));
    }

    /// <summary>
    /// Replaces what the requester filled in. Only a draft can be edited: once Budgets has reserved an amount
    /// and the route depends on it, changing the lines would leave both wrong.
    /// </summary>
    public void Edit(Actor actor, RequisitionDetails details, DateTimeOffset now)
    {
        EnsureRequester(actor);
        EnsureDraft();

        Checked valid = Validate(details, now);
        CostCentreCode = valid.CostCentreCode;
        SupplierId = details.SupplierId;
        Justification = valid.Justification;
        NeededBy = details.NeededBy;
        ReplaceLines(valid.Lines);
        Amount = valid.Amount;
        Record(TimelineAction.Edited, now, actor);
    }

    /// <summary>
    /// Sends the requisition to Budgets. The cost centre and supplier are the local copies as they stand now;
    /// either may be missing if its first event has not arrived yet, which is a refusal, not an error.
    /// </summary>
    public void Submit(Actor actor, CostCentre? costCentre, Supplier? supplier, DateTimeOffset now)
    {
        EnsureRequester(actor);
        EnsureDraft();

        if (costCentre is null)
        {
            throw Conflict(RequisitionCodes.CostCentreUnknown, $"Cost centre {CostCentreCode} is not known.");
        }

        if (costCentre.Code != CostCentreCode)
        {
            throw new ArgumentException($"Cost centre {costCentre.Code} is not this requisition's.", nameof(costCentre));
        }

        if (!costCentre.IsActive)
        {
            throw Conflict(RequisitionCodes.CostCentreInactive, $"Cost centre {CostCentreCode} is not active.");
        }

        if (supplier is null)
        {
            throw Conflict(RequisitionCodes.SupplierUnknown, $"Supplier {SupplierId} is not known.");
        }

        if (supplier.Id != SupplierId)
        {
            throw new ArgumentException($"Supplier {supplier.Id} is not this requisition's.", nameof(supplier));
        }

        if (!supplier.IsActive)
        {
            throw Conflict(RequisitionCodes.SupplierInactive, $"Supplier {supplier.LegalName} is not active.");
        }

        // The manager step can only be taken by the manager, and nobody approves their own requisition, so a
        // manager's own requisition could never be approved. Better to say so now than after funds are held.
        if (costCentre.ManagerId == RequesterId)
        {
            throw Conflict(
                RequisitionCodes.RequesterIsManager,
                $"You manage {CostCentreCode}, so nobody could take the manager step of your requisition.");
        }

        FiscalYear = now.UtcDateTime.Year;
        Status = RequisitionStatus.Submitted;
        Record(TimelineAction.Submitted, now, actor);
    }

    /// <summary>
    /// Budgets reserved the funds: fix the route from the cost centre's manager as the local copy has it now.
    /// Returns false, changing nothing, unless the requisition is waiting for exactly this.
    /// </summary>
    public bool RecordFundsReserved(Guid managerId, DateTimeOffset at)
    {
        // A cancelled requisition stays cancelled: Budgets saw the cancellation too and releases the
        // reservation itself. Any other state means this is a redelivery.
        if (Status != RequisitionStatus.Submitted)
        {
            return false;
        }

        IReadOnlyList<ApprovalStepKind> route = ApprovalRoute.For(Amount);
        for (int index = 0; index < route.Count; index++)
        {
            ApprovalStepKind kind = route[index];
            _steps.Add(new ApprovalStep(index + 1, kind, kind == ApprovalStepKind.Manager ? managerId : null));
        }

        Status = RequisitionStatus.PendingApproval;
        CurrentStep = 1;
        Record(TimelineAction.FundsReserved, at, actorId: null, actorName: null, $"Route: {string.Join(", ", route)}");
        return true;
    }

    /// <summary>Budgets refused the reservation. Returns false, changing nothing, unless waiting for it.</summary>
    public bool RecordFundsRefused(string reason, DateTimeOffset at)
    {
        if (Status != RequisitionStatus.Submitted)
        {
            return false;
        }

        Status = RequisitionStatus.BudgetRejected;
        RejectionReason = reason;
        Record(TimelineAction.FundsRefused, at, actorId: null, actorName: null, reason);
        return true;
    }

    /// <summary>Signs off the current step. Returns true when that was the last one.</summary>
    public bool Approve(Actor actor, DateTimeOffset now)
    {
        ApprovalStep step = StepFor(actor);
        step.Decide(ApprovalDecision.Approved, actor.Id, now);

        bool last = step.Sequence == _steps.Count;
        if (last)
        {
            Status = RequisitionStatus.Approved;
            CurrentStep = null;
        }
        else
        {
            CurrentStep = step.Sequence + 1;
        }

        Record(
            last ? TimelineAction.Approved : TimelineAction.StepApproved,
            now,
            actor,
            $"Step {step.Sequence} of {_steps.Count} ({step.Kind})");
        return last;
    }

    /// <summary>Refuses the requisition at its current step. The same people who could approve it may reject it.</summary>
    public void Reject(Actor actor, string reason, DateTimeOffset now)
    {
        ApprovalStep step = StepFor(actor);

        string trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxReasonLength)
        {
            throw Invalid(RequisitionCodes.ReasonRequired, $"A rejection needs a reason of at most {MaxReasonLength} characters.");
        }

        step.Decide(ApprovalDecision.Rejected, actor.Id, now);
        Status = RequisitionStatus.Rejected;
        CurrentStep = null;
        RejectionReason = trimmed;
        Record(TimelineAction.Rejected, now, actor, trimmed);
    }

    /// <summary>
    /// Withdraws the requisition. Returns whether Budgets may be holding a reservation for it, which is when
    /// Budgets has to be told.
    /// </summary>
    public bool Cancel(Actor actor, DateTimeOffset now)
    {
        EnsureRequester(actor);
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.Submitted or RequisitionStatus.PendingApproval))
        {
            throw Conflict(RequisitionCodes.NotCancellable, $"Requisition {Number} is {Status} and can no longer be cancelled.");
        }

        bool mightHoldReservation = Status != RequisitionStatus.Draft;
        Status = RequisitionStatus.Cancelled;
        CurrentStep = null;
        Record(TimelineAction.Cancelled, now, actor);
        return mightHoldReservation;
    }

    /// <summary>
    /// Purchasing issued an order for this requisition. Returns false, changing nothing, for a redelivery or
    /// for a message that could only arrive by replay before the requisition was approved here.
    /// </summary>
    public bool RecordPurchaseOrderIssued(Guid purchaseOrderId, string purchaseOrderNumber, Guid issuedBy, DateTimeOffset at)
    {
        // Closed is accepted too: the closing message can overtake this one, and the order's number is still
        // worth keeping. The status stays Closed.
        if (PurchaseOrderNumber is not null || Status is not (RequisitionStatus.Approved or RequisitionStatus.Closed))
        {
            return false;
        }

        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderNumber = purchaseOrderNumber;
        if (Status == RequisitionStatus.Approved)
        {
            Status = RequisitionStatus.Ordered;
        }

        Record(TimelineAction.Ordered, at, issuedBy, actorName: null, purchaseOrderNumber);
        return true;
    }

    /// <summary>
    /// Purchasing finished with the order, or cancelled it before issuing it. Returns false, changing nothing,
    /// unless the requisition was approved or ordered.
    /// </summary>
    public bool RecordPurchaseOrderClosed(Guid purchaseOrderId, string reason, DateTimeOffset at)
    {
        if (Status is not (RequisitionStatus.Approved or RequisitionStatus.Ordered))
        {
            return false;
        }

        Status = RequisitionStatus.Closed;
        PurchaseOrderId ??= purchaseOrderId;
        Record(TimelineAction.Closed, at, actorId: null, actorName: null, reason);
        return true;
    }

    private static Checked Validate(RequisitionDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);

        string costCentreCode = NormaliseCode(details.CostCentreCode);
        if (costCentreCode.Length == 0 || costCentreCode.Length > CostCentre.MaxCodeLength)
        {
            throw Invalid(RequisitionCodes.CostCentreInvalid, "A requisition needs a cost centre code.");
        }

        if (details.SupplierId == Guid.Empty)
        {
            throw Invalid(RequisitionCodes.SupplierInvalid, "A requisition needs a supplier.");
        }

        string justification = details.Justification?.Trim() ?? string.Empty;
        if (justification.Length == 0 || justification.Length > MaxJustificationLength)
        {
            throw Invalid(
                RequisitionCodes.JustificationInvalid,
                $"A requisition needs a justification of at most {MaxJustificationLength} characters.");
        }

        if (details.NeededBy < DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw Invalid(RequisitionCodes.NeededByInPast, $"The needed-by date {details.NeededBy:yyyy-MM-dd} has passed.");
        }

        if (details.Lines is null || details.Lines.Count is 0 or > MaxLines)
        {
            throw Invalid(RequisitionCodes.LineCountInvalid, $"A requisition has between 1 and {MaxLines} lines.");
        }

        List<RequisitionLine> lines = [.. details.Lines.Select(static (input, index) => RequisitionLine.Validated(index + 1, input))];
        decimal amount = lines.Sum(static line => line.Amount);
        if (amount > MaxAmount)
        {
            throw Invalid(RequisitionCodes.AmountTooLarge, $"The requisition comes to more than {MaxAmount}.");
        }

        return new Checked(costCentreCode, justification, lines, amount);
    }

    // Lines keep their number as their key, so an edit updates the lines it keeps, inserts the ones it adds
    // and deletes the ones it drops, rather than deleting and re-inserting every line.
    private void ReplaceLines(List<RequisitionLine> lines)
    {
        _lines.RemoveAll(line => line.LineNumber > lines.Count);
        foreach (RequisitionLine line in lines)
        {
            RequisitionLine? existing = _lines.Find(held => held.LineNumber == line.LineNumber);
            if (existing is null)
            {
                _lines.Add(line);
            }
            else
            {
                existing.CopyFrom(line);
            }
        }
    }

    private ApprovalStep StepFor(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status != RequisitionStatus.PendingApproval)
        {
            throw Conflict(RequisitionCodes.NotPendingApproval, $"Requisition {Number} is {Status}, not waiting for approval.");
        }

        if (actor.Id == RequesterId)
        {
            throw Forbidden(RequisitionCodes.SelfApproval, "Nobody approves or rejects their own requisition.");
        }

        if (_steps.Exists(step => step.DecidedBy == actor.Id))
        {
            throw Forbidden(
                RequisitionCodes.DuplicateApprover,
                "You already signed off a step of this requisition; the next one needs someone else.");
        }

        ApprovalStep current = _steps.Single(step => step.Sequence == CurrentStep);
        if (!current.IsFor(actor))
        {
            throw Forbidden(
                RequisitionCodes.NotYourStep,
                current.RequiredRole is { } role
                    ? $"Step {current.Sequence} needs the {role} role."
                    : $"Step {current.Sequence} waits for the manager of {CostCentreCode}.");
        }

        return current;
    }

    private void EnsureRequester(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Id != RequesterId)
        {
            throw Forbidden(RequisitionCodes.NotRequester, $"Only the requester may change requisition {Number}.");
        }
    }

    private void EnsureDraft()
    {
        if (Status != RequisitionStatus.Draft)
        {
            throw Conflict(RequisitionCodes.NotDraft, $"Requisition {Number} is {Status}, no longer a draft.");
        }
    }

    private void Record(TimelineAction action, DateTimeOffset at, Actor actor, string? detail = null) =>
        Record(action, at, actor.Id, actor.Name, detail);

    private void Record(TimelineAction action, DateTimeOffset at, Guid? actorId, string? actorName, string? detail)
    {
        Revision++;
        _timeline.Add(new TimelineEntry(Guid.CreateVersion7(at), Revision, at, action, actorId, actorName, detail));
    }

    private static string NormaliseCode(string? code) => code?.Trim().ToUpperInvariant() ?? string.Empty;

    private static BusinessRuleException Invalid(string code, string message) => new(code, message, ViolationKind.Invalid);

    private static BusinessRuleException Conflict(string code, string message) => new(code, message, ViolationKind.Conflict);

    private static BusinessRuleException Forbidden(string code, string message) => new(code, message, ViolationKind.Forbidden);

    private sealed record Checked(string CostCentreCode, string Justification, List<RequisitionLine> Lines, decimal Amount);
}
