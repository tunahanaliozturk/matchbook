using Matchbook.Contracts.Budgets;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>Budgets would not hold the funds, which ends the requisition.</summary>
public sealed class FundsReservationRejectedHandler(IRequisitionsDb db, ILogger<FundsReservationRejectedHandler> logger)
{
    public Task HandleAsync(FundsReservationRejected message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return db.RecordAsync(
            message.RequisitionId,
            nameof(FundsReservationRejected),
            logger,
            requisition => requisition.RecordFundsRefused(message.Reason, message.OccurredAt),
            cancellationToken);
    }
}
