namespace Matchbook.Contracts.Budgets;

/// <summary>
/// A cost centre was created or changed. Requisitions routes the first approval to <see cref="ManagerId"/>.
/// Apply only when <see cref="Version"/> is higher than the version held.
/// </summary>
public sealed record CostCentreChanged(
    string CostCentreCode,
    long Version,
    string Name,
    Guid ManagerId,
    bool IsActive,
    DateTimeOffset OccurredAt);

/// <summary>Funds were set aside for a submitted requisition (a pre-encumbrance).</summary>
public sealed record FundsReserved(
    Guid RequisitionId,
    string CostCentreCode,
    int FiscalYear,
    decimal Amount,
    DateTimeOffset OccurredAt);

/// <summary>A requisition could not be reserved for. <see cref="Reason"/> is one of <see cref="FundsRejectionReason"/>.</summary>
public sealed record FundsReservationRejected(
    Guid RequisitionId,
    string CostCentreCode,
    int FiscalYear,
    decimal Requested,
    decimal Available,
    string Reason,
    DateTimeOffset OccurredAt);

/// <summary>
/// A purchase order's amount is now committed (an encumbrance) and the requisition's reservation released, in
/// one step. <see cref="Attempt"/> echoes the request it answers.
/// </summary>
public sealed record FundsCommitted(
    Guid PurchaseOrderId,
    Guid RequisitionId,
    int Attempt,
    string CostCentreCode,
    int FiscalYear,
    decimal Amount,
    DateTimeOffset OccurredAt);

/// <summary>
/// A purchase order could not be committed; the reservation for its requisition stands. <see cref="Attempt"/>
/// echoes the request it answers, so a stale rejection that arrives after a later attempt succeeded can be
/// ignored.
/// </summary>
public sealed record FundsCommitmentRejected(
    Guid PurchaseOrderId,
    Guid RequisitionId,
    int Attempt,
    decimal Requested,
    decimal Available,
    string Reason,
    DateTimeOffset OccurredAt);

/// <summary>Values of the <c>Reason</c> on a rejected reservation or commitment.</summary>
public static class FundsRejectionReason
{
    /// <summary>The budget exists but has too little left.</summary>
    public const string InsufficientFunds = "insufficient_funds";

    /// <summary>No budget was set for the cost centre in that fiscal year.</summary>
    public const string NoBudget = "no_budget";

    /// <summary>The cost centre is unknown or inactive.</summary>
    public const string CostCentreUnavailable = "cost_centre_unavailable";

    /// <summary>The document was already cancelled or closed when the request was processed.</summary>
    public const string DocumentClosed = "document_closed";
}
