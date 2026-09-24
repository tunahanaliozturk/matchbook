using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Requisitions;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matchbook.Budgets.Application.IntegrationEvents;

/// <summary>Reserves a submitted requisition's amount, and answers with <c>FundsReserved</c> or a refusal.</summary>
public sealed class RequisitionSubmittedHandler(
    IBudgetsDb db,
    IEventPublisher events,
    TimeProvider clock,
    ILogger<RequisitionSubmittedHandler> logger) : IIntegrationEventHandler<RequisitionSubmitted>
{
    public Task HandleAsync(RequisitionSubmitted message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return db.InTransactionAsync(() => ReserveAsync(message, cancellationToken), cancellationToken);
    }

    private async Task ReserveAsync(RequisitionSubmitted message, CancellationToken cancellationToken)
    {
        Movement movement = Movement.Reserve(message.Amount);
        RequisitionReservation? earlier = await db.Reservations.FindAsync([message.RequisitionId], cancellationToken);
        if (earlier is not null)
        {
            // A redelivery changes nothing; its answer went out with the change it answered. After a release,
            // though, this is the late reservation the tombstone is there to refuse.
            if (earlier.Status == ReservationStatus.Released)
            {
                await RefuseAsync(message, FundsRefusal.DocumentClosed, available: 0m, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        CostCentre? costCentre = await db.CostCentres.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == message.CostCentreCode, cancellationToken);
        Budget? budget = await db.Budgets.AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.CostCentreCode == message.CostCentreCode && b.FiscalYear == message.FiscalYear,
                cancellationToken);

        if (FundsCheck.RefusesReservation(costCentre, budget, out FundsRefusal refusal))
        {
            db.Reservations.Add(RequisitionReservation.Refuse(message.RequisitionId, message.Amount, refusal));
            await RefuseAsync(message, refusal, available: 0m, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        LedgerEntry entry = LedgerEntry.Record(
            budget.Id, message.RequisitionId, LedgerStep.Reserve, movement, message.OccurredAt, now);
        if (await db.TryApplyWithinAvailableAsync(entry, "reservation", cancellationToken))
        {
            db.Reservations.Add(RequisitionReservation.Hold(message.RequisitionId, budget.Id, message.Amount));
            db.Ledger.Add(entry);
            BudgetsMetrics.ReservationGranted();
            await events.PublishAsync(
                new FundsReserved(message.RequisitionId, message.CostCentreCode, message.FiscalYear, message.Amount, now),
                cancellationToken);
        }
        else
        {
            db.Reservations.Add(
                RequisitionReservation.Refuse(message.RequisitionId, message.Amount, FundsRefusal.InsufficientFunds));
            await RefuseAsync(
                message,
                FundsRefusal.InsufficientFunds,
                await db.AvailableAsync(budget.Id, cancellationToken),
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private Task RefuseAsync(
        RequisitionSubmitted message, FundsRefusal refusal, decimal available, CancellationToken cancellationToken)
    {
        logger.ReservationRefused(
            message.Amount, message.RequisitionId, message.CostCentreCode, message.FiscalYear, refusal);
        BudgetsMetrics.ReservationRefused(refusal);
        return events.PublishAsync(
            new FundsReservationRejected(
                message.RequisitionId,
                message.CostCentreCode,
                message.FiscalYear,
                message.Amount,
                available,
                refusal.ToReason(),
                clock.GetUtcNow()),
            cancellationToken);
    }
}
