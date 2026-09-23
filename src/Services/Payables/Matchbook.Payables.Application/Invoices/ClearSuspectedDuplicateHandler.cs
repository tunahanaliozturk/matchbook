using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Invoices;

/// <summary>An AP approver who did not capture the invoice confirms it is not a duplicate, and it goes on to the match.</summary>
public sealed class ClearSuspectedDuplicateHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
{
    public async Task<InvoiceView> HandleAsync(Guid invoiceId, Actor approver, CancellationToken cancellationToken)
    {
        Invoice invoice = await db.Invoices.SingleOrNotFoundAsync(invoiceId, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();

        invoice.ClearSuspectedDuplicate(approver, now);
        PurchaseOrder order = await matcher.ClaimOrderAsync(invoice.PurchaseOrderId, cancellationToken);
        await matcher.MatchAsync(order, invoice, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return InvoiceView.From(invoice);
    }
}
