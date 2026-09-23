namespace Matchbook.Purchasing.Domain;

/// <summary>
/// Where a purchase order is in its life. <see cref="Completed"/>, <see cref="ShortClosed"/> and
/// <see cref="Cancelled"/> are ends; nothing moves an order out of them.
/// </summary>
public enum PurchaseOrderStatus
{
    /// <summary>Drafted from an approved requisition. A buyer may change quantities and prices.</summary>
    Draft,

    /// <summary>A buyer issued it and Budgets has not answered the commitment request yet.</summary>
    CommitmentPending,

    /// <summary>Funds are committed and the order is with the supplier. Goods can be received.</summary>
    Issued,

    /// <summary>Every line was received and invoiced in full.</summary>
    Completed,

    /// <summary>
    /// A buyer closed it before it settled. Nothing more will be received; invoices for what was received still
    /// count.
    /// </summary>
    ShortClosed,

    /// <summary>A buyer cancelled it before anything was received.</summary>
    Cancelled,
}
