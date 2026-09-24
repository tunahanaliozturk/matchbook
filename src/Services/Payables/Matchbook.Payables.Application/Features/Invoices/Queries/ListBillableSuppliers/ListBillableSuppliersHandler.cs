using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListBillableSuppliers;

/// <summary>
/// The suppliers a clerk chooses from when capturing an invoice. An AP clerk cannot read Suppliers, so the list comes
/// from the copy Payables keeps (ADR 0009), and only suppliers with an order still open to invoices are offered.
/// </summary>
public sealed class ListBillableSuppliersHandler(IPayablesDb db) : IQueryHandler<ListBillableSuppliersQuery, Page<BillableSupplier>>
{
    public async Task<Page<BillableSupplier>> HandleAsync(ListBillableSuppliersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        IQueryable<PurchaseOrder> billable = db.PurchaseOrders.Billable();
        IQueryable<Supplier> suppliers = db.Suppliers
            .AsNoTracking()
            .Where(supplier => billable.Any(order => order.SupplierId == supplier.Id));
        if (query.After is { } after)
        {
            suppliers = suppliers.Where(supplier => supplier.Id.CompareTo(after) < 0);
        }

        List<BillableSupplier> rows = await suppliers
            .OrderByDescending(supplier => supplier.Id)
            .Take(limit + 1)
            .Select(supplier => new BillableSupplier(supplier.Id, supplier.LegalName, supplier.IsActive, supplier.PaymentTermsDays))
            .ToListAsync(cancellationToken);

        return Paging.ToPage(rows, limit, supplier => supplier.Id);
    }
}
