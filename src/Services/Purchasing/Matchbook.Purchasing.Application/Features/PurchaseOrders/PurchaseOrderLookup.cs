using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders;

internal static class PurchaseOrderLookup
{
    /// <summary>Loads an order, tracked and with its lines, for a change, or refuses with a 404.</summary>
    public static async Task<PurchaseOrder> GetForChangeAsync(
        this DbSet<PurchaseOrder> orders, Guid id, CancellationToken cancellationToken) =>
        await orders.SingleOrDefaultAsync(order => order.Id == id, cancellationToken)
        ?? throw NotFound(id);

    public static BusinessRuleException NotFound(Guid id) =>
        new("purchase_order.not_found", $"There is no purchase order {id}.", ViolationKind.NotFound);
}
