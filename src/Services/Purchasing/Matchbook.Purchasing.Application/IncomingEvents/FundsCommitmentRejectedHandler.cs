using Matchbook.Contracts.Budgets;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Microsoft.Extensions.Logging;

namespace Matchbook.Purchasing.Application.IncomingEvents;

/// <summary>
/// Returns the order to draft, with Budgets' reason, when the commitment it is waiting for is refused. The
/// requisition's reservation still stands, so the buyer can change the order and issue it again.
/// </summary>
public sealed class FundsCommitmentRejectedHandler(
    IPurchasingDb db, ILogger<FundsCommitmentRejectedHandler> logger)
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
    }
}
