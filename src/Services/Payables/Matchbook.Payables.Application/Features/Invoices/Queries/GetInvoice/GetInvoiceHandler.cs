using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.GetInvoice;

public sealed class GetInvoiceHandler(IPayablesDb db) : IQueryHandler<GetInvoiceQuery, InvoiceView>
{
    public async Task<InvoiceView> HandleAsync(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Invoice invoice = await db.Invoices.AsNoTracking().SingleOrNotFoundAsync(query.InvoiceId, cancellationToken);
        return await db.ViewAsync(invoice, cancellationToken);
    }
}
