using Matchbook.Contracts.Purchasing;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.Application.Events;

/// <summary>Records the issued order and matches the invoices that were waiting for it.</summary>
public sealed class PurchaseOrderIssuedHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
{
    public async Task HandleAsync(PurchaseOrderIssued message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        PurchaseOrder order = await matcher.ClaimOrderAsync(message.PurchaseOrderId, cancellationToken);
        bool recorded = order.RecordIssue(
            message.Number,
            message.SupplierId,
            message.Lines.Select(line => new OrderedLine(line.LineNumber, line.Quantity, line.UnitPrice)),
            message.OccurredAt.ToUniversalTime());
        if (!recorded)
        {
            return;
        }

        await matcher.RematchWaitingAsync(order, arrived: null, time.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
