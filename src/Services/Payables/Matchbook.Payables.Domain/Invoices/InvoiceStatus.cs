namespace Matchbook.Payables.Domain.Invoices;

/// <summary>Where an invoice is on its way from capture to payment. Stored by name.</summary>
public enum InvoiceStatus
{
    /// <summary>Just captured and not yet looked at. Never stored: capture either holds it or matches it.</summary>
    Captured,

    /// <summary>Looks like another invoice from the same supplier. Held until an AP approver clears it.</summary>
    SuspectedDuplicate,

    /// <summary>The purchase order it bills has not arrived yet. Matched again when it does.</summary>
    AwaitingPurchaseOrder,

    /// <summary>More is invoiced on a line than was received. Matched again when goods arrive.</summary>
    AwaitingReceipt,

    /// <summary>A price is outside tolerance. Waits for an AP approver to accept it with a reason.</summary>
    PriceVariance,

    /// <summary>It can never match: the order was cancelled, or it bills another supplier's order or a line the order does not have.</summary>
    Rejected,

    /// <summary>Matched and owed. Picked up by the first payment run drafted on or after its due date.</summary>
    Payable,

    /// <summary>In a draft payment run.</summary>
    Scheduled,

    /// <summary>Paid by a released payment run.</summary>
    Paid,
}
