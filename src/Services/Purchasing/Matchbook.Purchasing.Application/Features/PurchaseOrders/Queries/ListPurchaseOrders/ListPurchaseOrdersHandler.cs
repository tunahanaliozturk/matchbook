using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListPurchaseOrders;

public sealed class ListPurchaseOrdersHandler(IPurchasingDb db) : IQueryHandler<ListPurchaseOrdersQuery, PurchaseOrderPage>
{
    public const int MaxLimit = 200;

    public async Task<PurchaseOrderPage> HandleAsync(ListPurchaseOrdersQuery query, CancellationToken cancellationToken)
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

        // One row past the page tells whether there is a next page without a second count query. The supplier's
        // name is a lookup by primary key, not a join, so an order for a supplier not heard of yet still lists.
        List<PurchaseOrderSummary> rows = await orders
            .OrderByDescending(order => order.Id)
            .Take(limit + 1)
            .Select(order => new PurchaseOrderSummary(
                order.Id,
                order.Number,
                order.Status,
                order.SupplierId,
                db.Suppliers.Where(supplier => supplier.Id == order.SupplierId).Select(supplier => supplier.LegalName).SingleOrDefault(),
                order.CostCentreCode,
                order.Amount,
                order.DraftedAt))
            .ToListAsync(cancellationToken);

        return rows.Count > limit
            ? new PurchaseOrderPage(rows[..limit], rows[limit - 1].Id)
            : new PurchaseOrderPage(rows, null);
    }
}
