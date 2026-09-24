using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListBillablePurchaseOrders;

/// <summary>
/// The orders a clerk chooses from when capturing an invoice, with the lines to bill. An AP clerk cannot read
/// Purchasing, so they come from the copy Payables matches against (ADR 0009).
/// </summary>
public sealed class ListBillablePurchaseOrdersHandler(IPayablesDb db)
    : IQueryHandler<ListBillablePurchaseOrdersQuery, Page<BillablePurchaseOrder>>
{
    public async Task<Page<BillablePurchaseOrder>> HandleAsync(ListBillablePurchaseOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        IQueryable<PurchaseOrder> orders = db.PurchaseOrders.AsNoTracking().Billable();
        if (query.SupplierId is { } supplierId)
        {
            orders = orders.Where(order => order.SupplierId == supplierId);
        }

        if (query.After is { } after)
        {
            orders = orders.Where(order => order.Id.CompareTo(after) < 0);
        }

        // Billable() admits issued orders only, and issuing sets the number, the supplier and the time.
        List<BillablePurchaseOrder> rows = await orders
            .OrderByDescending(order => order.Id)
            .Take(limit + 1)
            .Select(order => new BillablePurchaseOrder(
                order.Id,
                order.Number!,
                order.SupplierId!.Value,
                order.IssuedAt!.Value,
                order.Lines
                    .OrderBy(line => line.LineNumber)
                    .Select(line => new BillablePurchaseOrderLine(line.LineNumber, line.Quantity, line.UnitPrice))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return Paging.ToPage(rows, limit, order => order.Id);
    }
}
