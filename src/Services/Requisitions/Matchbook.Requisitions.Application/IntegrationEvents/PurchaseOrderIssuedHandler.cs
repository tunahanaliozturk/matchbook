using Matchbook.Contracts.Purchasing;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IntegrationEvents;

/// <summary>Purchasing ordered what the requisition asked for.</summary>
public sealed class PurchaseOrderIssuedHandler(IRequisitionsDb db, ILogger<PurchaseOrderIssuedHandler> logger)
    : IIntegrationEventHandler<PurchaseOrderIssued>
{
    public Task HandleAsync(PurchaseOrderIssued integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return db.RecordAsync(
            integrationEvent.RequisitionId,
            nameof(PurchaseOrderIssued),
            logger,
            requisition => requisition.RecordPurchaseOrderIssued(
                integrationEvent.PurchaseOrderId, integrationEvent.Number, integrationEvent.IssuedBy, integrationEvent.OccurredAt),
            cancellationToken);
    }
}
