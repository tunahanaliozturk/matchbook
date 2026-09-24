using Matchbook.Contracts.Purchasing;
using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.Application.Features.Invoices;

internal static class BillableOrders
{
    /// <summary>
    /// Orders an invoice can still bill: issued, and neither cancelled nor completed. A short-closed order stays in,
    /// because what was received before the close is still owed and still matches.
    /// </summary>
    public static IQueryable<PurchaseOrder> Billable(this IQueryable<PurchaseOrder> orders) =>
        orders.Where(order => order.IssuedAt != null
            && !order.IsCancelled
            && order.CloseReason != PurchaseOrderCloseReason.Completed);
}
