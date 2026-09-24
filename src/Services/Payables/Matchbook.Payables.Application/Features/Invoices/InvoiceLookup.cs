using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices;

internal static class InvoiceLookup
{
    public static async Task<Invoice> SingleOrNotFoundAsync(
        this IQueryable<Invoice> invoices,
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        await invoices.SingleOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken)
        ?? throw new BusinessRuleException("invoice.not_found", "There is no such invoice.", ViolationKind.NotFound);

    /// <summary>
    /// The invoice as the API answers with it, with the names people know its supplier and order by. Either may be
    /// missing, because the local copies arrive by events in any order.
    /// </summary>
    public static async Task<InvoiceView> ViewAsync(this IPayablesDb db, Invoice invoice, CancellationToken cancellationToken)
    {
        string? supplierName = await db.Suppliers
            .Where(supplier => supplier.Id == invoice.SupplierId)
            .Select(supplier => supplier.LegalName)
            .SingleOrDefaultAsync(cancellationToken);
        string? orderNumber = await db.PurchaseOrders
            .Where(order => order.Id == invoice.PurchaseOrderId)
            .Select(order => order.Number)
            .SingleOrDefaultAsync(cancellationToken);

        return InvoiceView.From(invoice, supplierName, orderNumber);
    }

    /// <summary>Fills in the supplier names of a page of summaries, with one query for the page.</summary>
    public static async Task<List<InvoiceSummary>> NamedAsync(
        this IPayablesDb db,
        List<InvoiceSummary> rows,
        CancellationToken cancellationToken)
    {
        Guid[] supplierIds = [.. rows.Select(row => row.SupplierId).Distinct()];
        Dictionary<Guid, string> names = await db.Suppliers
            .Where(supplier => supplierIds.Contains(supplier.Id))
            .Select(supplier => new { supplier.Id, supplier.LegalName })
            .ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.LegalName, cancellationToken);

        return [.. rows.Select(row => row with { SupplierName = names.GetValueOrDefault(row.SupplierId) })];
    }
}
