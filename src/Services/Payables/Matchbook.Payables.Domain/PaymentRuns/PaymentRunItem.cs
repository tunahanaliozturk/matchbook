using Matchbook.Payables.Domain.Invoices;

namespace Matchbook.Payables.Domain.PaymentRuns;

/// <summary>
/// One invoice in a payment run. Its status follows its creditor's decision at release, or the run's cancellation;
/// those changes are applied to all of a run's items at once by the application.
/// </summary>
/// <remarks>
/// An item that is <see cref="PaymentRunItemStatus.Scheduled"/> or <see cref="PaymentRunItemStatus.Paid"/> holds its
/// invoice, and the database allows one such item per invoice. That index is what makes paying an invoice twice
/// impossible rather than merely unlikely.
/// </remarks>
public sealed class PaymentRunItem
{
    private PaymentRunItem(
        Guid paymentRunId,
        Guid invoiceId,
        Guid purchaseOrderId,
        Guid supplierId,
        string supplierInvoiceNumber,
        decimal amount,
        PaymentRunItemStatus status)
    {
        PaymentRunId = paymentRunId;
        InvoiceId = invoiceId;
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        SupplierInvoiceNumber = supplierInvoiceNumber;
        Amount = amount;
        Status = status;
    }

    public Guid PaymentRunId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>The supplier's own number, sent back to them as the remittance information.</summary>
    public string SupplierInvoiceNumber { get; private set; }

    /// <summary>The invoice's matched total: an invoice is paid exactly what it was matched for.</summary>
    public decimal Amount { get; private set; }

    public PaymentRunItemStatus Status { get; private set; }

    internal static PaymentRunItem Schedule(Guid paymentRunId, PaymentCandidate candidate) =>
        new(
            paymentRunId,
            candidate.InvoiceId,
            candidate.PurchaseOrderId,
            candidate.SupplierId,
            candidate.SupplierInvoiceNumber,
            candidate.Amount,
            PaymentRunItemStatus.Scheduled);
}

public enum PaymentRunItemStatus
{
    Scheduled,
    Paid,

    /// <summary>Its supplier was dropped at release. The invoice is payable again.</summary>
    Dropped,

    /// <summary>Its run was cancelled. The invoice is payable again.</summary>
    Cancelled,
}

/// <summary>A payable invoice as a payment run draft needs to see it.</summary>
public sealed record PaymentCandidate(
    Guid InvoiceId,
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierInvoiceNumber,
    decimal Amount,
    InvoiceStatus Status,
    DateOnly InvoiceDate,
    DateOnly? DueDate);
