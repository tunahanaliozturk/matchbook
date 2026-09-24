using Matchbook.Contracts.Budgets;
using Matchbook.Purchasing.Application.Common;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Purchasing.Application.IntegrationEvents;

/// <summary>
/// Issues the order when Budgets confirms the commitment it is waiting for, and publishes
/// <c>PurchaseOrderIssued</c> with the buyer who issued it.
/// </summary>
/// <remarks>
/// A reply for another attempt, a redelivered reply, or a reply that finds the order already cancelled changes
/// nothing and publishes nothing. For the cancelled case Budgets still ends up holding nothing: see
/// <c>docs/services/purchasing.md</c>.
/// </remarks>
public sealed class FundsCommittedHandler(
    IPurchasingDb db,
    IEventPublisher publisher,
    TimeProvider clock,
    PurchasingMetrics metrics,
    ILogger<FundsCommittedHandler> logger)
    : IIntegrationEventHandler<FundsCommitted>
{
    public async Task HandleAsync(FundsCommitted message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await db.PurchaseOrders.GetForChangeAsync(message.PurchaseOrderId, cancellationToken);

        if (!order.ConfirmCommitment(message.Attempt, clock.GetUtcNow()))
        {
            logger.CommitmentReplyIgnored(
                nameof(FundsCommitted), order.Id, message.Attempt, order.Status, order.CommitmentAttempt);
            return;
        }

        await publisher.PublishAsync(OutgoingEvents.Issued(order), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        metrics.OrderIssued(order);
    }
}
