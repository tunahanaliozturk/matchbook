using Matchbook.Contracts.Purchasing;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>Purchasing is finished with the requisition's order, or cancelled it before issuing it.</summary>
public sealed class PurchaseOrderClosedHandler(IRequisitionsDb db, ILogger<PurchaseOrderClosedHandler> logger)
{
    public Task HandleAsync(PurchaseOrderClosed message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return db.RecordAsync(
            message.RequisitionId,
            nameof(PurchaseOrderClosed),
            logger,
            requisition => requisition.RecordPurchaseOrderClosed(message.PurchaseOrderId, message.Reason, message.OccurredAt),
            cancellationToken);
    }
}
