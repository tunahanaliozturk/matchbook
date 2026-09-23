namespace Matchbook.Requisitions.Domain;

/// <summary>
/// Where a requisition is in its life. <see cref="BudgetRejected"/>, <see cref="Rejected"/>,
/// <see cref="Cancelled"/> and <see cref="Closed"/> are ends: nothing moves a requisition out of them.
/// </summary>
public enum RequisitionStatus
{
    /// <summary>Being written by its requester. Nothing outside this service knows it exists.</summary>
    Draft,

    /// <summary>Sent to Budgets for a reservation, and waiting for the answer.</summary>
    Submitted,

    /// <summary>Funds are reserved and the approval route is fixed; waiting on its current step.</summary>
    PendingApproval,

    /// <summary>Every step signed off. Purchasing drafts an order from it.</summary>
    Approved,

    /// <summary>Purchasing issued an order for it.</summary>
    Ordered,

    /// <summary>The order is finished with, or was cancelled before it was issued.</summary>
    Closed,

    /// <summary>Budgets refused the reservation.</summary>
    BudgetRejected,

    /// <summary>An approver said no.</summary>
    Rejected,

    /// <summary>The requester withdrew it before approval.</summary>
    Cancelled,
}
