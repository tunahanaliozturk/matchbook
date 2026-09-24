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
}
