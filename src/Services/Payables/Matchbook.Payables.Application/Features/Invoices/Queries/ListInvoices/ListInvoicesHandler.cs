using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListInvoices;

public sealed class ListInvoicesHandler(IPayablesDb db) : IQueryHandler<ListInvoicesQuery, Page<InvoiceSummary>>
{
    public async Task<Page<InvoiceSummary>> HandleAsync(ListInvoicesQuery query, CancellationToken cancellationToken)
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
