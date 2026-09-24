using Matchbook.Contracts.Purchasing;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.IntegrationEvents;

/// <summary>
/// Stores a receipt, whether or not its order has arrived, and matches the invoices that were waiting for goods.
/// A receipt already stored is a redelivery and changes nothing.
/// </summary>
public sealed class GoodsReceivedHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
    : IIntegrationEventHandler<GoodsReceived>
{
    public async Task HandleAsync(GoodsReceived message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (await db.Receipts.AnyAsync(receipt => receipt.Id == message.ReceiptId, cancellationToken))
        {
            return;
        }

        var receipt = Receipt.Record(
            message.ReceiptId,
            message.PurchaseOrderId,
            message.Lines.Select(line => new ReceivedLine(line.LineNumber, line.Quantity)),
            message.OccurredAt.ToUniversalTime());
        db.Receipts.Add(receipt);

        // Claimed even when the order has not arrived: an order arriving at the same moment then conflicts with this
        // receipt instead of summing the receipts without it.
        PurchaseOrder order = await matcher.ClaimOrderAsync(message.PurchaseOrderId, cancellationToken);
        await matcher.RematchWaitingAsync(order, receipt, RematchTriggers.GoodsReceived, time.GetUtcNow(), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }
}
