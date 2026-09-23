using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

public sealed record AmendDraftLine(Guid PurchaseOrderId, int LineNumber, decimal Quantity, decimal UnitPrice);

public sealed class AmendDraftLineHandler(IPurchasingDb db)
{
    public async Task<PurchaseOrderView> HandleAsync(
        AmendDraftLine command, Actor buyer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        order.AmendLine(buyer, command.LineNumber, command.Quantity, command.UnitPrice);

        await db.SaveChangesAsync(cancellationToken);
        return PurchaseOrderView.From(order);
    }
}
