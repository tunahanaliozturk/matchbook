namespace Matchbook.Contracts.Requisitions;

/// <summary>A requester submitted a requisition. Budgets reserves <see cref="Amount"/> for it.</summary>
public sealed record RequisitionSubmitted(
    Guid RequisitionId,
    string Number,
    Guid RequesterId,
    string CostCentreCode,
    int FiscalYear,
    Guid SupplierId,
    decimal Amount,
    DateTimeOffset OccurredAt);

/// <summary>Every approval step signed off. Purchasing drafts a purchase order from it.</summary>
public sealed record RequisitionApproved(
    Guid RequisitionId,
    string Number,
    Guid RequesterId,
    string CostCentreCode,
    int FiscalYear,
    Guid SupplierId,
    IReadOnlyList<RequisitionLine> Lines,
    decimal Amount,
    IReadOnlyList<Guid> ApproverIds,
    DateTimeOffset OccurredAt);

/// <summary>A requisition line. <see cref="Amount"/> is <c>Amounts.Line(Quantity, UnitPrice)</c>.</summary>
public sealed record RequisitionLine(
    int LineNumber,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal Amount);

/// <summary>An approver said no. Budgets releases the reservation.</summary>
public sealed record RequisitionRejected(
    Guid RequisitionId,
    Guid RejectedBy,
    string Reason,
    DateTimeOffset OccurredAt);

/// <summary>
/// The requester withdrew a submitted requisition before it was approved. Budgets releases the reservation,
/// and refuses one that arrives later.
/// </summary>
public sealed record RequisitionCancelled(
    Guid RequisitionId,
    Guid CancelledBy,
    DateTimeOffset OccurredAt);
