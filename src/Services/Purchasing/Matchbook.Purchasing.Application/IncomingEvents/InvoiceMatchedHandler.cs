using Matchbook.Contracts.Payables;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matchbook.Purchasing.Application.IncomingEvents;

/// <summary>
/// Counts a matched invoice's quantities as invoiced, once per invoice, and publishes
/// <c>PurchaseOrderClosed</c> when that completes the order.
/// </summary>
/// <remarks>
/// The order always exists: Payables can only match against an order it heard of from
/// <c>PurchaseOrderIssued</c>, which Purchasing publishes after the order is saved. So a missing order, like an
/// invoice that would exceed what was received, is a defect and fails the message rather than being waited for.
/// </remarks>
public sealed class InvoiceMatchedHandler(
    IPurchasingDb db, IEventPublisher publisher, TimeProvider clock, ILogger<InvoiceMatchedHandler> logger)
{
    public async Task HandleAsync(InvoiceMatched message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (await db.MatchedInvoices.AnyAsync(invoice => invoice.InvoiceId == message.InvoiceId, cancellationToken))
        {
            logger.AlreadyApplied(nameof(InvoiceMatched), message.InvoiceId);
            return;
        }

        var order = await db.PurchaseOrders.GetForChangeAsync(message.PurchaseOrderId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();

        bool completed = order.RecordInvoice(
            [.. message.Lines.Select(static line => new LineQuantity(line.LineNumber, line.Quantity))], now);
        db.MatchedInvoices.Add(new MatchedInvoice(message.InvoiceId, order.Id, now));

        if (completed)
        {
            await publisher.PublishAsync(OutgoingEvents.Closed(order), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
