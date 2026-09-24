using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListInvoiceExceptions;

/// <summary>
/// The AP approvers' work: invoices held as suspected duplicates or on a price variance, oldest first. Invoices
/// waiting for an order or for goods are not here, because they move on their own when those arrive.
/// </summary>
public sealed class ListInvoiceExceptionsHandler(IPayablesDb db) : IQueryHandler<ListInvoiceExceptionsQuery, Page<InvoiceSummary>>
{
    public async Task<Page<InvoiceSummary>> HandleAsync(ListInvoiceExceptionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int take = Paging.Clamp(query.Limit);
        IQueryable<Invoice> exceptions = db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.SuspectedDuplicate || invoice.Status == InvoiceStatus.PriceVariance);
        if (query.After is { } cursor)
        {
            exceptions = exceptions.Where(invoice => invoice.Id.CompareTo(cursor) > 0);
        }

        List<InvoiceSummary> rows = await exceptions
            .OrderBy(invoice => invoice.Id)
            .Take(take + 1)
            .Select(InvoiceSummary.Projection)
            .ToListAsync(cancellationToken);

        return Paging.ToPage(rows, take, summary => summary.Id);
    }
}
