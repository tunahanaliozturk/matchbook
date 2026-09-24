using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Payables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matchbook.Budgets.Application.Funds;

/// <summary>
/// Moves a matched invoice from its order's commitment to actual. Never refused: an invoice that passed the
/// match is a fact, even when it overspends the budget.
/// </summary>
public sealed class InvoiceMatchedHandler(IBudgetsDb db, TimeProvider clock, ILogger<InvoiceMatchedHandler> logger)
{
    public Task HandleAsync(InvoiceMatched message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return db.InTransactionAsync(() => MatchAsync(message, cancellationToken), cancellationToken);
    }

    private async Task MatchAsync(InvoiceMatched message, CancellationToken cancellationToken)
    {
        // Payables matches only issued orders, and an order is issued only after this service committed it, so
        // an unknown order here is a broken promise upstream. It fails loudly onto the error queue rather than
        // guessing a budget.
        OrderCommitment order = await db.Commitments.FindAsync([message.PurchaseOrderId], cancellationToken)
            ?? throw OrderCommitment.NeverCommitted(message.PurchaseOrderId);

        if (await db.Ledger.AnyAsync(
                e => e.DocumentId == message.InvoiceId && e.Step == LedgerStep.Invoice, cancellationToken))
        {
            return;
        }

        LedgerEntry entry = order.Invoice(message.InvoiceId, message.Amount, message.OccurredAt, clock.GetUtcNow());
        await db.ApplyAsync(entry, cancellationToken);
        db.Ledger.Add(entry);

        // Only the part of an invoice beyond its commitment consumes funds, so only an invoice with such a part
        // can push the budget over. The read is inside the transaction and sees the update just made.
        if (entry.Movement.Consumes > 0m)
        {
            decimal available = await db.AvailableAsync(entry.BudgetId, cancellationToken);
            if (available < 0m && available + entry.Movement.Consumes >= 0m)
            {
                BudgetsMetrics.Overspent();
                logger.BudgetOverspent(message.InvoiceId, entry.BudgetId, -available);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
