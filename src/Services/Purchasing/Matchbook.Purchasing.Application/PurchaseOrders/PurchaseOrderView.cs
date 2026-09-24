using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>A purchase order as the API returns it, from a read or after any change.</summary>
public sealed record PurchaseOrderView(
    Guid Id,
    string Number,
    PurchaseOrderStatus Status,
    Guid RequisitionId,
    Guid SupplierId,
    string CostCentreCode,
    int FiscalYear,
    decimal Amount,
    int CommitmentAttempt,
    string? CommitmentRejectionReason,
    Guid? IssuedBy,
    DateTimeOffset DraftedAt,
    DateTimeOffset? IssuedAt,
    Guid? ClosedBy,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<OrderLineView> Lines)
{
    public static PurchaseOrderView From(PurchaseOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new(
            order.Id,
            order.Number,
            order.Status,
            order.RequisitionId,
            order.SupplierId,
            order.CostCentreCode,
            order.FiscalYear,
            order.Amount,
            order.CommitmentAttempt,
            order.CommitmentRejectionReason,
            order.IssuedBy,
            order.DraftedAt,
            order.IssuedAt,
            order.ClosedBy,
            order.ClosedAt,
            [.. order.Lines.OrderBy(static line => line.LineNumber).Select(OrderLineView.From)]);
    }
}

/// <summary>An order line with what was received and invoiced against it so far.</summary>
public sealed record OrderLineView(
    int LineNumber,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal Amount,
    decimal ReceivedQuantity,
    decimal InvoicedQuantity)
{
    public static OrderLineView From(OrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new(
            line.LineNumber,
            line.Description,
            line.Quantity,
            line.UnitOfMeasure,
            line.UnitPrice,
            line.Amount,
            line.ReceivedQuantity,
            line.InvoicedQuantity);
    }
}
