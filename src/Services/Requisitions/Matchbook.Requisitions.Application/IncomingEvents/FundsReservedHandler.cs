using Matchbook.Contracts.Budgets;
using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>Funds are held, so the approval route is fixed and the first approver can act.</summary>
public sealed class FundsReservedHandler(IRequisitionsDb db, ILogger<FundsReservedHandler> logger)
{
    public async Task HandleAsync(FundsReserved message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Budgets echoes the cost centre from RequisitionSubmitted, and a submitted requisition cannot change
        // its cost centre, so the message's code is the requisition's. Submitting required the local copy, and
        // copies are never deleted: a missing one is a broken invariant for a person to look at, so the
        // message fails and is parked rather than dropped.
        CostCentre costCentre = await db.CostCentres
            .AsNoTracking()
            .FirstOrDefaultAsync(centre => centre.Code == message.CostCentreCode, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Funds were reserved for requisition {message.RequisitionId} on cost centre {message.CostCentreCode}, which has no local copy.");

        await db.RecordAsync(
            message.RequisitionId,
            nameof(FundsReserved),
            logger,
            requisition => requisition.RecordFundsReserved(costCentre.ManagerId, message.OccurredAt),
            cancellationToken);
    }
}
