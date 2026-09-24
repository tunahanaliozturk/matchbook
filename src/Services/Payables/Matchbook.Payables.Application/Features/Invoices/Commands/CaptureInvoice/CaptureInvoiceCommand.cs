using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Commands.CaptureInvoice;

/// <param name="Id">
/// Optional, chosen by the client so a retry after a lost response returns the invoice instead of capturing a second
/// one. A version 7 GUID keeps the invoice list in capture order.
/// </param>
public sealed record CaptureInvoiceCommand(
    Guid? Id,
    Guid SupplierId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    Guid PurchaseOrderId,
    IReadOnlyList<CaptureInvoiceLine> Lines,
    decimal Total,
    Actor Clerk) : ICommand<InvoiceView>;

public sealed record CaptureInvoiceLine(int LineNumber, decimal Quantity, decimal UnitPrice);
