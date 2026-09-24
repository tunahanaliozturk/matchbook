using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Commands.ClearSuspectedDuplicate;

/// <summary>An AP approver who did not capture the invoice confirms it is not a duplicate, and it goes on to the match.</summary>
public sealed class ClearSuspectedDuplicateHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
    : ICommandHandler<ClearSuspectedDuplicateCommand, InvoiceView>
{
    public async Task<InvoiceView> HandleAsync(ClearSuspectedDuplicateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Invoice invoice = await db.Invoices.SingleOrNotFoundAsync(command.InvoiceId, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();

        invoice.ClearSuspectedDuplicate(command.Approver, now);
        PurchaseOrder order = await matcher.ClaimOrderAsync(invoice.PurchaseOrderId, cancellationToken);
        await matcher.MatchAsync(order, invoice, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return InvoiceView.From(invoice);
    }
}
