using Matchbook.Contracts.Payables;
using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices;

/// <summary>
/// Everything that can change whether an invoice matches goes through here: capturing it, an approver releasing it,
/// and every fact that arrives about its order. It claims the order's local row, works out what has been received
/// and invoiced per line, runs the match and publishes <see cref="InvoiceMatched"/> for each invoice that passed.
/// </summary>
public sealed class InvoiceMatcher(IPayablesDb db, IEventPublisher publisher, PayablesMetrics metrics)
{
    /// <summary>
    /// Loads the order's local row for change, creating it on the first mention. Either way the row is written in
    /// this unit of work, which is what serialises concurrent matches on the order (see <see cref="PurchaseOrder"/>).
    /// </summary>
    public async Task<PurchaseOrder> ClaimOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        PurchaseOrder? order = await db.PurchaseOrders.SingleOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken);
        if (order is null)
        {
            order = PurchaseOrder.FirstMentioned(purchaseOrderId);
            db.PurchaseOrders.Add(order);
        }
        else
        {
            order.Revise();
        }

        return order;
    }

    /// <summary>Matches one invoice against a claimed order.</summary>
    public async Task MatchAsync(PurchaseOrder order, Invoice invoice, DateTimeOffset now, CancellationToken cancellationToken)
    {
        OrderPosition position = await PositionAsync(order.Id, cancellationToken);
        await EvaluateAsync(order, position, [invoice], now, cancellationToken);
    }

    /// <summary>
    /// Matches again every invoice waiting on a claimed order. <paramref name="arrived"/> is a receipt added in this
    /// unit of work and not yet saved, so the position has to be told about it. <paramref name="trigger"/> names what
    /// arrived, for the metrics.
    /// </summary>
    public async Task RematchWaitingAsync(
        PurchaseOrder order,
        Receipt? arrived,
        string trigger,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<Invoice> waiting = await db.Invoices
            .Where(invoice => invoice.PurchaseOrderId == order.Id
                && (invoice.Status == InvoiceStatus.AwaitingPurchaseOrder
                    || invoice.Status == InvoiceStatus.AwaitingReceipt
                    || invoice.Status == InvoiceStatus.PriceVariance))
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
        {
            return;
        }

        OrderPosition position = await PositionAsync(order.Id, cancellationToken);
        if (arrived is not null)
        {
            position.RecordReceived(arrived);
        }

        await EvaluateAsync(order, position, waiting, now, cancellationToken);
        metrics.Rematched(trigger, waiting.Count);
    }

    private async Task EvaluateAsync(
        PurchaseOrder order,
        OrderPosition position,
        IReadOnlyCollection<Invoice> invoices,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int? paymentTermsDays = order.SupplierId is { } supplierId
            ? await db.Suppliers
                .Where(supplier => supplier.Id == supplierId)
                .Select(supplier => (int?)supplier.PaymentTermsDays)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        IReadOnlyList<Invoice> matched = InvoiceMatching.Evaluate(invoices, order, position, paymentTermsDays, now);
        foreach (Invoice invoice in invoices)
        {
            metrics.Evaluated(invoice);
        }

        foreach (Invoice invoice in matched)
        {
            await publisher.PublishAsync(
                new InvoiceMatched(
                    invoice.Id,
                    invoice.PurchaseOrderId,
                    invoice.SupplierId,
                    invoice.Number,
                    [.. invoice.Lines.Select(line => new MatchedLine(line.LineNumber, line.Quantity, line.UnitPrice, line.Amount))],
                    invoice.Total,
                    now),
                cancellationToken);
        }
    }

    // Two grouped sums, whatever the number of receipts and invoices on the order.
    private async Task<OrderPosition> PositionAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        Dictionary<int, decimal> received = await db.Receipts
            .Where(receipt => receipt.PurchaseOrderId == purchaseOrderId)
            .SelectMany(receipt => receipt.Lines)
            .GroupBy(line => line.LineNumber)
            .Select(group => new { LineNumber = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(line => line.LineNumber, line => line.Quantity, cancellationToken);

        Dictionary<int, decimal> invoiced = await db.Invoices
            .Where(invoice => invoice.PurchaseOrderId == purchaseOrderId
                && (invoice.Status == InvoiceStatus.Payable
                    || invoice.Status == InvoiceStatus.Scheduled
                    || invoice.Status == InvoiceStatus.Paid))
            .SelectMany(invoice => invoice.Lines)
            .GroupBy(line => line.LineNumber)
            .Select(group => new { LineNumber = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(line => line.LineNumber, line => line.Quantity, cancellationToken);

        return new OrderPosition(received, invoiced);
    }
}
