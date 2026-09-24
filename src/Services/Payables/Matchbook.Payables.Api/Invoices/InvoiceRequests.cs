using System.ComponentModel.DataAnnotations;
using Matchbook.Payables.Application.Invoices;

namespace Matchbook.Payables.Api.Invoices;

// Requests check only their shape: what is present and of the right type is a 400 with the failing fields. Whether
// the values make a valid invoice (totals, scales, line numbers) is the domain's answer, a 422 with a code.

/// <summary>A supplier invoice as the clerk keys it in.</summary>
/// <param name="Id">Optional. Repeating a capture with the same id returns the invoice it created instead of a second one.</param>
public sealed record CaptureInvoiceRequest(
    Guid? Id,
    [Required] Guid? SupplierId,
    [Required] string? SupplierInvoiceNumber,
    [Required] DateOnly? InvoiceDate,
    [Required] Guid? PurchaseOrderId,
    [Required] IReadOnlyList<CaptureInvoiceLineRequest>? Lines,
    [Required] decimal? Total)
{
    internal CaptureInvoice ToCommand() =>
        new(
            Id,
            SupplierId!.Value,
            SupplierInvoiceNumber!,
            InvoiceDate!.Value,
            PurchaseOrderId!.Value,
            [.. Lines!.Select(static line => new CaptureInvoiceLine(line.LineNumber!.Value, line.Quantity!.Value, line.UnitPrice!.Value))],
            Total!.Value);
}

/// <summary>One billed line: the purchase order line it bills, the quantity and the unit price.</summary>
public sealed record CaptureInvoiceLineRequest(
    [Required] int? LineNumber,
    [Required] decimal? Quantity,
    [Required] decimal? UnitPrice);

/// <summary>Why the approver accepts a price outside tolerance. Kept on the invoice.</summary>
public sealed record AcceptPriceVarianceRequest([Required] string? Reason);
