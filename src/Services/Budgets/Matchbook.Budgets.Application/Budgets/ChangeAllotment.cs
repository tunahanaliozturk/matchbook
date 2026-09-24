using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Budgets;

/// <summary>
/// Raises (positive <paramref name="Change"/>) or lowers (negative) a budget's allotment. A change rather than a
/// new total, so two admins adjusting the same budget at once both get what they asked for.
/// </summary>
public sealed record ChangeAllotment(Guid BudgetId, decimal Change);

public sealed class ChangeAllotmentHandler(IBudgetsDb db, TimeProvider clock)
{
    public Task<BudgetView> HandleAsync(ChangeAllotment command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        return db.InTransactionAsync(() => ChangeAsync(command, actor, cancellationToken), cancellationToken);
    }

    private async Task<BudgetView> ChangeAsync(ChangeAllotment command, Actor actor, CancellationToken cancellationToken)
    {
        Budget budget = await db.FindBudgetAsync(command.BudgetId, cancellationToken);
        Movement movement = budget.ChangeAllotment(command.Change);
        DateTimeOffset now = clock.GetUtcNow();
        LedgerEntry entry = LedgerEntry.Record(
            budget.Id, Guid.CreateVersion7(now), LedgerStep.Allot, movement, now, now, actor.Id);

        // Lowering races with grants: the figures just read may already be out of date, so the database checks
        // the floor again in the UPDATE itself. Raising can only help, and is never refused.
        if (command.Change > 0m)
        {
            await db.ApplyAsync(entry, cancellationToken);
        }
        else if (!await db.TryApplyWithinAvailableAsync(entry, cancellationToken))
        {
            throw new BusinessRuleException(
                "concurrency.conflict",
                "Funds were reserved or committed on this budget while its allotment was being lowered. Read it again and retry.");
        }

        db.Ledger.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return BudgetView.From(await db.FindBudgetAsync(budget.Id, cancellationToken));
    }
}
