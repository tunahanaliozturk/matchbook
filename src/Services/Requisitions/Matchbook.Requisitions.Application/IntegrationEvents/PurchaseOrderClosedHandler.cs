using Matchbook.Contracts.Purchasing;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IntegrationEvents;

/// <summary>Purchasing is finished with the requisition's order, or cancelled it before issuing it.</summary>
public sealed class PurchaseOrderClosedHandler(IRequisitionsDb db, ILogger<PurchaseOrderClosedHandler> logger)
    : IIntegrationEventHandler<PurchaseOrderClosed>
{
    public Task HandleAsync(PurchaseOrderClosed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return db.RecordAsync(
            integrationEvent.RequisitionId,
            nameof(PurchaseOrderClosed),
            logger,
            requisition => requisition.RecordPurchaseOrderClosed(
                integrationEvent.PurchaseOrderId, integrationEvent.Reason, integrationEvent.OccurredAt),
            cancellationToken);
    }
}
