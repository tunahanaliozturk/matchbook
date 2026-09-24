using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Invoices;

public sealed class GetInvoiceHandler(IPayablesDb db)
{
    public async Task<InvoiceView> HandleAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        InvoiceView.From(await db.Invoices.AsNoTracking().SingleOrNotFoundAsync(invoiceId, cancellationToken));
}
