using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matchbook.Budgets.Application.Funds;

/// <summary>
/// Turns a requisition's reservation into its order's commitment in one ledger entry, and answers with
/// <c>FundsCommitted</c> or <c>FundsCommitmentRejected</c> carrying the request's attempt.
/// </summary>
public sealed class PurchaseOrderCommitmentRequestedHandler(
    IBudgetsDb db,
    IEventPublisher events,
    TimeProvider clock,
    ILogger<PurchaseOrderCommitmentRequestedHandler> logger)
{
    public Task HandleAsync(PurchaseOrderCommitmentRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return db.InTransactionAsync(() => CommitAsync(message, cancellationToken), cancellationToken);
    }

    private async Task CommitAsync(PurchaseOrderCommitmentRequested message, CancellationToken cancellationToken)
    {
        Money.Positive(message.Amount, "The order amount");
        OrderCommitment? known = await db.Commitments.FindAsync([message.PurchaseOrderId], cancellationToken);
        OrderCommitment order = known ?? OrderCommitment.Track(message.PurchaseOrderId, message.RequisitionId);
        switch (order.Replay(message.Attempt))
        {
            case CommitmentReplay.AlreadyCommitted:
                // Purchasing may ask again, with a new attempt, for an order whose first answer it never saw.
                await PublishCommittedAsync(message, order.Amount, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return;
            case CommitmentReplay.Closed:
                await RefuseAsync(message, order, FundsRefusal.DocumentClosed, available: 0m, cancellationToken);
                return;
            case CommitmentReplay.Stale:
                return;
        }

        if (known is null)
        {
            db.Commitments.Add(order);
        }

        RequisitionReservation? reservation = await db.Reservations.FindAsync([message.RequisitionId], cancellationToken);
        Budget? budget = await db.Budgets.AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.CostCentreCode == message.CostCentreCode && b.FiscalYear == message.FiscalYear,
                cancellationToken);

        if (FundsCheck.RefusesCommitment(reservation, budget, out FundsRefusal refusal))
        {
            await RefuseAsync(message, order, refusal, available: 0m, cancellationToken);
            return;
        }

        decimal held = reservation?.Held ?? 0m;
        LedgerEntry entry = LedgerEntry.Record(
            budget.Id,
            message.PurchaseOrderId,
            LedgerStep.Commit,
            Movement.Commit(held, message.Amount),
            message.OccurredAt,
            clock.GetUtcNow());

        // The guard is "amount <= available + reservation": the entry releases the reservation and commits the
        // order's amount, so it consumes only the difference.
        if (!await db.TryApplyWithinAvailableAsync(entry, "commitment", cancellationToken))
        {
            decimal available = await db.AvailableAsync(budget.Id, cancellationToken) + held;
            await RefuseAsync(message, order, FundsRefusal.InsufficientFunds, available, cancellationToken);
            return;
        }

        order.Commit(budget.Id, message.Attempt, message.Amount);
        reservation?.HandOver();
        db.Ledger.Add(entry);
        BudgetsMetrics.CommitmentGranted();
        await PublishCommittedAsync(message, message.Amount, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task PublishCommittedAsync(
        PurchaseOrderCommitmentRequested message, decimal committed, CancellationToken cancellationToken) =>
        events.PublishAsync(
            new FundsCommitted(
                message.PurchaseOrderId,
                message.RequisitionId,
                message.Attempt,
                message.CostCentreCode,
                message.FiscalYear,
                committed,
                clock.GetUtcNow()),
            cancellationToken);

    private async Task RefuseAsync(
        PurchaseOrderCommitmentRequested message,
        OrderCommitment order,
        FundsRefusal refusal,
        decimal available,
        CancellationToken cancellationToken)
    {
        if (order.Status == CommitmentStatus.Uncommitted)
        {
            order.Refuse(message.Attempt, refusal);
        }

        logger.CommitmentRefused(message.Amount, message.PurchaseOrderId, message.Attempt, refusal);
        BudgetsMetrics.CommitmentRefused(refusal);
        await events.PublishAsync(
            new FundsCommitmentRejected(
                message.PurchaseOrderId,
                message.RequisitionId,
                message.Attempt,
                message.Amount,
                available,
                refusal.ToReason(),
                clock.GetUtcNow()),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
