using Matchbook.Contracts.Purchasing;
using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>
/// Turns the state of an order into the integration events Purchasing publishes, in one place, so every
/// handler that publishes the same event publishes the same shape.
/// </summary>
internal static class OutgoingEvents
{
    public static PurchaseOrderCommitmentRequested CommitmentRequested(PurchaseOrder order, DateTimeOffset now) =>
        new(
            order.Id,
            order.RequisitionId,
            order.CommitmentAttempt,
            order.CostCentreCode,
            order.FiscalYear,
            order.Amount,
            now);

    public static PurchaseOrderIssued Issued(PurchaseOrder order) =>
        new(
            order.Id,
            order.Number,
            order.RequisitionId,
            order.SupplierId,
            order.CostCentreCode,
            order.FiscalYear,
            [
                .. order.Lines
                    .OrderBy(static line => line.LineNumber)
                    .Select(static line => new PurchaseOrderLine(
                        line.LineNumber,
                        line.Description,
                        line.Quantity,
                        line.UnitOfMeasure,
                        line.UnitPrice,
                        line.Amount)),
            ],
            order.Amount,
            order.IssuedBy ?? throw NotYet(order, "issued"),
            order.IssuedAt ?? throw NotYet(order, "issued"));

    public static GoodsReceived GoodsReceived(GoodsReceipt receipt) =>
        new(
            receipt.Id,
            receipt.PurchaseOrderId,
            [.. receipt.Lines.Select(static line => new ReceiptLine(line.LineNumber, line.Quantity))],
            receipt.ReceivedBy,
            receipt.ReceivedAt);

    public static PurchaseOrderClosed Closed(PurchaseOrder order) =>
        new(
            order.Id,
            order.RequisitionId,
            order.Status switch
            {
                PurchaseOrderStatus.Completed => PurchaseOrderCloseReason.Completed,
                PurchaseOrderStatus.ShortClosed => PurchaseOrderCloseReason.ShortClosed,
                PurchaseOrderStatus.Cancelled => PurchaseOrderCloseReason.Cancelled,
                _ => throw NotYet(order, "closed"),
            },
            order.ClosedAt ?? throw NotYet(order, "closed"));

    private static InvalidOperationException NotYet(PurchaseOrder order, string what) =>
        new($"Purchase order {order.Id} is {order.Status}, not {what}.");
}
