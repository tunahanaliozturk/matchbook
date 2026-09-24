using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.IssuePurchaseOrder;

/// <summary>
/// Sends a draft to Budgets for commitment. The order is not issued yet: it waits in
/// <c>CommitmentPending</c> until <c>FundsCommitted</c> or <c>FundsCommitmentRejected</c> comes back.
/// </summary>
public sealed class IssuePurchaseOrderHandler(IPurchasingDb db, IEventPublisher publisher, TimeProvider clock)
    : ICommandHandler<IssuePurchaseOrderCommand, PurchaseOrderView>
{
    public async Task<PurchaseOrderView> HandleAsync(IssuePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await db.PurchaseOrders.GetForChangeAsync(command.PurchaseOrderId, cancellationToken);
        var supplier = await db.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(supplier => supplier.Id == order.SupplierId, cancellationToken);

        order.RequestIssue(command.Buyer, supplier);

        await publisher.PublishAsync(OutgoingEvents.CommitmentRequested(order, clock.GetUtcNow()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return PurchaseOrderView.From(order, supplier?.LegalName);
    }
}
