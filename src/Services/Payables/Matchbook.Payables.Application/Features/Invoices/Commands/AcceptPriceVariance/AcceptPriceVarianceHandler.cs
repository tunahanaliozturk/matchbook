using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Commands.AcceptPriceVariance;

/// <summary>An AP approver who did not capture the invoice accepts its price variance, with a reason, and it is matched again.</summary>
public sealed class AcceptPriceVarianceHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
    : ICommandHandler<AcceptPriceVarianceCommand, InvoiceView>
{
    public async Task<InvoiceView> HandleAsync(AcceptPriceVarianceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Invoice invoice = await db.Invoices.SingleOrNotFoundAsync(command.InvoiceId, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();

        invoice.AcceptPriceVariance(command.Approver, command.Reason, now);
        PurchaseOrder order = await matcher.ClaimOrderAsync(invoice.PurchaseOrderId, cancellationToken);
        await matcher.MatchAsync(order, invoice, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return InvoiceView.From(invoice);
    }
}
