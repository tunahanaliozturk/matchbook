using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.Application.Features.Requisitions;

/// <summary>A requisition in full, with its lines, its route and its timeline, as every command returns it.</summary>
public sealed record RequisitionView(
    Guid Id,
    string Number,
    RequisitionStatus Status,
    Guid RequesterId,
    string RequesterName,
    string CostCentreCode,
    Guid SupplierId,
    string Justification,
    DateOnly NeededBy,
    decimal Amount,
    int? FiscalYear,
    string? RejectionReason,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    DateTimeOffset CreatedAt,
    int Revision,
    IReadOnlyList<LineView> Lines,
    IReadOnlyList<StepView> Steps,
    IReadOnlyList<TimelineEntryView> Timeline)
{
    internal static RequisitionView From(Requisition requisition) => new(
        requisition.Id,
        requisition.Number,
        requisition.Status,
        requisition.RequesterId,
        requisition.RequesterName,
        requisition.CostCentreCode,
        requisition.SupplierId,
        requisition.Justification,
        requisition.NeededBy,
        requisition.Amount,
        requisition.FiscalYear,
        requisition.RejectionReason,
        requisition.PurchaseOrderId,
        requisition.PurchaseOrderNumber,
        requisition.CreatedAt,
        requisition.Revision,
        [.. requisition.Lines
            .OrderBy(static line => line.LineNumber)
            .Select(static line => new LineView(
                line.LineNumber, line.Description, line.Quantity, line.UnitOfMeasure, line.UnitPrice, line.Amount))],
        [.. requisition.Steps
            .OrderBy(static step => step.Sequence)
            .Select(static step => new StepView(
                step.Sequence, step.Kind, step.ApproverId, step.RequiredRole, step.Decision, step.DecidedBy, step.DecidedAt))],
        [.. requisition.Timeline
            .OrderBy(static entry => entry.Sequence)
            .Select(static entry => new TimelineEntryView(
                entry.Sequence, entry.At, entry.Action, entry.ActorId, entry.ActorName, entry.Detail))]);
}

public sealed record LineView(
    int LineNumber,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal Amount);

public sealed record StepView(
    int Sequence,
    ApprovalStepKind Kind,
    Guid? ApproverId,
    string? RequiredRole,
    ApprovalDecision Decision,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt);

public sealed record TimelineEntryView(
    int Sequence,
    DateTimeOffset At,
    TimelineAction Action,
    Guid? ActorId,
    string? ActorName,
    string? Detail);
