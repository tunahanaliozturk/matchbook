using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Invoices;

/// <summary>An AP approver who did not capture the invoice accepts its price variance, with a reason, and it is matched again.</summary>
public sealed class AcceptPriceVarianceHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
{
    public async Task<InvoiceView> HandleAsync(
        Guid invoiceId,
        string reason,
        Actor approver,
        CancellationToken cancellationToken)
    {
        Invoice invoice = await db.Invoices.SingleOrNotFoundAsync(invoiceId, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();

        invoice.AcceptPriceVariance(approver, reason, now);
        PurchaseOrder order = await matcher.ClaimOrderAsync(invoice.PurchaseOrderId, cancellationToken);
        await matcher.MatchAsync(order, invoice, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return InvoiceView.From(invoice);
    }
}
