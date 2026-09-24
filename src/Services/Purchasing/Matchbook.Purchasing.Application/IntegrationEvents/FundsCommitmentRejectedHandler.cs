using Matchbook.Contracts.Budgets;
using Matchbook.Purchasing.Application.Common;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Purchasing.Application.IntegrationEvents;

/// <summary>
/// Returns the order to draft, with Budgets' reason, when the commitment it is waiting for is refused. The
/// requisition's reservation still stands, so the buyer can change the order and issue it again.
/// </summary>
public sealed class FundsCommitmentRejectedHandler(
    IPurchasingDb db, PurchasingMetrics metrics, ILogger<FundsCommitmentRejectedHandler> logger)
    : IIntegrationEventHandler<FundsCommitmentRejected>
{
    public async Task HandleAsync(FundsCommitmentRejected message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await db.PurchaseOrders.GetForChangeAsync(message.PurchaseOrderId, cancellationToken);

        if (!order.RejectCommitment(message.Attempt, message.Reason))
        {
            logger.CommitmentReplyIgnored(
                nameof(FundsCommitmentRejected), order.Id, message.Attempt, order.Status, order.CommitmentAttempt);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        metrics.CommitmentRejected(message.Reason);
    }
}
