using Matchbook.Contracts.Purchasing;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.IntegrationEvents;

/// <summary>Records the issued order and matches the invoices that were waiting for it.</summary>
public sealed class PurchaseOrderIssuedHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
    : IIntegrationEventHandler<PurchaseOrderIssued>
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

        await matcher.RematchWaitingAsync(order, arrived: null, RematchTriggers.OrderIssued, time.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
