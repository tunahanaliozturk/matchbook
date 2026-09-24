using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.AmendDraftLine;

public sealed class AmendDraftLineHandler(IPurchasingDb db) : ICommandHandler<AmendDraftLineCommand, PurchaseOrderView>
{
    public async Task<PurchaseOrderView> HandleAsync(AmendDraftLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        order.AmendLine(command.Buyer, command.LineNumber, command.Quantity, command.UnitPrice);

        await db.SaveChangesAsync(cancellationToken);
        return PurchaseOrderView.From(order);
    }
}
