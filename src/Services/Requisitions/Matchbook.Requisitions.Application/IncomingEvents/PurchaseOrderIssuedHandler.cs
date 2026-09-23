using Matchbook.Contracts.Purchasing;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>Purchasing ordered what the requisition asked for.</summary>
public sealed class PurchaseOrderIssuedHandler(IRequisitionsDb db, ILogger<PurchaseOrderIssuedHandler> logger)
{
    public Task HandleAsync(PurchaseOrderIssued message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return db.RecordAsync(
            message.RequisitionId,
            nameof(PurchaseOrderIssued),
            logger,
            requisition => requisition.RecordPurchaseOrderIssued(
                message.PurchaseOrderId, message.Number, message.IssuedBy, message.OccurredAt),
            cancellationToken);
    }
}
