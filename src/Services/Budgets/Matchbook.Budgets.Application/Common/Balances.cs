using System.Diagnostics;
using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Common;

/// <summary>
/// The only way a budget's figures change: one UPDATE per ledger entry, adding the entry's movement to the row.
/// Budgets are never loaded, edited in memory and saved back, because two handlers doing that at once would
/// each write figures computed from a row the other had already changed.
/// </summary>
internal static class Balances
{
    /// <summary>
    /// Applies the entry only if the budget can afford it, and says whether it did. The check and the change are
    /// one statement. Postgres locks the row for the UPDATE; a second grant on the same budget waits for the first
    /// to commit, then re-reads the row and evaluates the WHERE clause again against the new figures (READ
    /// COMMITTED), so two grants can never both spend the same headroom. The condition is
    /// <see cref="Budget.CanAfford"/> written for the database.
    /// </summary>
    /// <param name="grant">What is being granted, for the latency histogram.</param>
    public static async Task<bool> TryApplyWithinAvailableAsync(
        this IBudgetsDb db, LedgerEntry entry, string grant, CancellationToken cancellationToken)
    {
        decimal needed = entry.Movement.Consumes;
        long started = Stopwatch.GetTimestamp();
        int updated = await AddAsync(
            db.Budgets.Where(b => b.Id == entry.BudgetId && b.Allotted - b.Reserved - b.Committed - b.Actual >= needed),
            entry.Movement,
            cancellationToken);
        BudgetsMetrics.GrantTook(Stopwatch.GetElapsedTime(started), grant, granted: updated == 1);
        return updated == 1;
    }

    /// <summary>Applies an entry that is never refused: a release, an invoice, a raise of the allotment.</summary>
    public static async Task ApplyAsync(this IBudgetsDb db, LedgerEntry entry, CancellationToken cancellationToken)
    {
        int updated = await AddAsync(db.Budgets.Where(b => b.Id == entry.BudgetId), entry.Movement, cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException($"Budget {entry.BudgetId} does not exist.");
        }
    }

    /// <summary>
    /// Available as it is now. Inside the transaction that just updated the row, that includes the update.
    /// </summary>
    public static Task<decimal> AvailableAsync(this IBudgetsDb db, Guid budgetId, CancellationToken cancellationToken) =>
        db.Budgets
            .Where(b => b.Id == budgetId)
            .Select(b => b.Allotted - b.Reserved - b.Committed - b.Actual)
            .SingleAsync(cancellationToken);

    private static Task<int> AddAsync(IQueryable<Budget> budget, Movement movement, CancellationToken cancellationToken) =>
        budget.ExecuteUpdateAsync(
            set => set
                .SetProperty(b => b.Allotted, b => b.Allotted + movement.Allotted)
                .SetProperty(b => b.Reserved, b => b.Reserved + movement.Reserved)
                .SetProperty(b => b.Committed, b => b.Committed + movement.Committed)
                .SetProperty(b => b.Actual, b => b.Actual + movement.Actual),
            cancellationToken);
}
