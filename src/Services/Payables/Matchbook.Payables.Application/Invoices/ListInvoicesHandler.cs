using Matchbook.Payables.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Invoices;

/// <summary>A page of invoices, newest first, optionally in one status.</summary>
public sealed record ListInvoices(InvoiceStatus? Status, Guid? After, int Limit);

public sealed class ListInvoicesHandler(IPayablesDb db)
{
    public async Task<Page<InvoiceSummary>> HandleAsync(ListInvoices query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        IQueryable<Invoice> invoices = db.Invoices.AsNoTracking();
        if (query.Status is { } status)
        {
            invoices = invoices.Where(invoice => invoice.Status == status);
        }

        // Ids are version 7 GUIDs, so ordering by id is ordering by capture time, and the id alone is the cursor.
        if (query.After is { } after)
        {
            invoices = invoices.Where(invoice => invoice.Id.CompareTo(after) < 0);
        }

        List<InvoiceSummary> rows = await invoices
            .OrderByDescending(invoice => invoice.Id)
            .Take(limit + 1)
            .Select(InvoiceSummary.Projection)
            .ToListAsync(cancellationToken);

        return Paging.ToPage(rows, limit, summary => summary.Id);
    }
}
