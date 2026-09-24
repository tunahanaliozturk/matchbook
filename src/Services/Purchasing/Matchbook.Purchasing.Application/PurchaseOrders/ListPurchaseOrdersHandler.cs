using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

/// <summary>A page of orders, newest first, optionally of one status.</summary>
/// <param name="After">The <see cref="PurchaseOrderPage.Next"/> of the previous page; null for the first.</param>
public sealed record ListPurchaseOrders(PurchaseOrderStatus? Status, Guid? After, int Limit);

public sealed record PurchaseOrderSummary(
    Guid Id,
    string Number,
    PurchaseOrderStatus Status,
    Guid SupplierId,
    string CostCentreCode,
    decimal Amount,
    DateTimeOffset DraftedAt);

/// <param name="Next">The cursor for the following page, or null when this is the last.</param>
public sealed record PurchaseOrderPage(IReadOnlyList<PurchaseOrderSummary> Items, Guid? Next);

public sealed class ListPurchaseOrdersHandler(IPurchasingDb db)
{
    public const int MaxLimit = 200;

    public async Task<PurchaseOrderPage> HandleAsync(ListPurchaseOrders query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Math.Clamp(query.Limit, 1, MaxLimit);
        IQueryable<PurchaseOrder> orders = db.PurchaseOrders.AsNoTracking();

        if (query.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        // Keyset on the id: version 7 ids sort by creation time, so "older than the last one seen" is one index
        // range scan however deep the reader pages, where an offset would read and discard every earlier row.
        if (query.After is { } after)
        {
            orders = orders.Where(order => order.Id.CompareTo(after) < 0);
        }

        // One row past the page tells whether there is a next page without a second count query.
        List<PurchaseOrderSummary> rows = await orders
            .OrderByDescending(order => order.Id)
            .Take(limit + 1)
            .Select(order => new PurchaseOrderSummary(
                order.Id,
                order.Number,
                order.Status,
                order.SupplierId,
                order.CostCentreCode,
                order.Amount,
                order.DraftedAt))
            .ToListAsync(cancellationToken);

        return rows.Count > limit
            ? new PurchaseOrderPage(rows[..limit], rows[limit - 1].Id)
            : new PurchaseOrderPage(rows, null);
    }
}
