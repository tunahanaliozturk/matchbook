using Matchbook.Contracts.Purchasing;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.IntegrationEvents;

/// <summary>
/// Records that an order was closed, even one Payables has not seen issued, and matches the invoices waiting on it:
/// a cancellation rejects them, any other close leaves them as they were.
/// </summary>
public sealed class PurchaseOrderClosedHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
    : IIntegrationEventHandler<PurchaseOrderClosed>
{
    public async Task HandleAsync(PurchaseOrderClosed message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Only a cancellation changes a match. An unknown reason is treated like a short close, which changes nothing:
        // rejecting on a value we cannot read would be the less cautious choice.
        bool cancelled = string.Equals(message.Reason, PurchaseOrderCloseReason.Cancelled, StringComparison.Ordinal);

        PurchaseOrder order = await matcher.ClaimOrderAsync(message.PurchaseOrderId, cancellationToken);
        if (!order.RecordClosure(message.Reason, cancelled, message.OccurredAt.ToUniversalTime()))
        {
            return;
        }

        await matcher.RematchWaitingAsync(order, arrived: null, RematchTriggers.OrderClosed, time.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
