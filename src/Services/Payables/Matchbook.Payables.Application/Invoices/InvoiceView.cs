using System.Linq.Expressions;
using Matchbook.Payables.Domain.Invoices;

namespace Matchbook.Payables.Application.Invoices;

/// <summary>An invoice with everything a person needs to act on it, including why it is where it is.</summary>
public sealed record InvoiceView(
    Guid Id,
    Guid SupplierId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    Guid PurchaseOrderId,
    decimal Total,
    IReadOnlyList<InvoiceLineView> Lines,
    InvoiceStatus Status,
    MatchReason? Reason,
    string? ReasonDetail,
    Guid CapturedBy,
    DateTimeOffset CapturedAt,
    Guid? SuspectedDuplicateOf,
    Guid? DuplicateClearedBy,
    Guid? VarianceAcceptedBy,
    string? VarianceAcceptanceReason,
    DateTimeOffset? MatchedAt,
    DateOnly? DueDate,
    DateTimeOffset? PaidAt)
{
    public static InvoiceView From(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new InvoiceView(
            invoice.Id,
            invoice.SupplierId,
            invoice.Number,
            invoice.InvoiceDate,
            invoice.PurchaseOrderId,
            invoice.Total,
            [.. invoice.Lines.Select(line => new InvoiceLineView(line.LineNumber, line.Quantity, line.UnitPrice, line.Amount))],
            invoice.Status,
            invoice.Reason,
            invoice.ReasonDetail,
            invoice.CapturedBy,
            invoice.CapturedAt,
            invoice.SuspectedDuplicateOf,
            invoice.DuplicateClearedBy,
            invoice.VarianceAcceptedBy,
            invoice.VarianceAcceptanceReason,
            invoice.MatchedAt,
            invoice.DueDate,
            invoice.PaidAt);
    }
}

public sealed record InvoiceLineView(int LineNumber, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <summary>An invoice as a list shows it.</summary>
public sealed record InvoiceSummary(
    Guid Id,
    Guid SupplierId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    Guid PurchaseOrderId,
    decimal Total,
    InvoiceStatus Status,
    MatchReason? Reason,
    DateTimeOffset CapturedAt,
    DateOnly? DueDate)
{
    internal static readonly Expression<Func<Invoice, InvoiceSummary>> Projection = invoice => new InvoiceSummary(
        invoice.Id,
        invoice.SupplierId,
        invoice.Number,
        invoice.InvoiceDate,
        invoice.PurchaseOrderId,
        invoice.Total,
        invoice.Status,
        invoice.Reason,
        invoice.CapturedAt,
        invoice.DueDate);
}
