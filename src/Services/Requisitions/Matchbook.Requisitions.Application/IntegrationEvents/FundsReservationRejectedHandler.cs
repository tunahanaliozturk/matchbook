using Matchbook.Contracts.Budgets;
using Matchbook.Requisitions.Application.Common;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IntegrationEvents;

/// <summary>Budgets would not hold the funds, which ends the requisition.</summary>
public sealed class FundsReservationRejectedHandler(
    IRequisitionsDb db,
    RequisitionMetrics metrics,
    ILogger<FundsReservationRejectedHandler> logger) : IIntegrationEventHandler<FundsReservationRejected>
{
    public async Task HandleAsync(FundsReservationRejected integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        bool recorded = await db.RecordAsync(
            integrationEvent.RequisitionId,
            nameof(FundsReservationRejected),
            logger,
            requisition => requisition.RecordFundsRefused(integrationEvent.Reason, integrationEvent.OccurredAt),
            cancellationToken);

        if (recorded)
        {
            metrics.BudgetRejected(integrationEvent.Reason);
        }
    }
}
