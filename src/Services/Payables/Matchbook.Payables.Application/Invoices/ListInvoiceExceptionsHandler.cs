using Matchbook.Payables.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Invoices;

/// <summary>
/// The AP approvers' work: invoices held as suspected duplicates or on a price variance, oldest first. Invoices
/// waiting for an order or for goods are not here, because they move on their own when those arrive.
/// </summary>
public sealed class ListInvoiceExceptionsHandler(IPayablesDb db)
{
    public async Task<Page<InvoiceSummary>> HandleAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        int take = Paging.Clamp(limit);
        IQueryable<Invoice> exceptions = db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.SuspectedDuplicate || invoice.Status == InvoiceStatus.PriceVariance);
        if (after is { } cursor)
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
