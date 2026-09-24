using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Budgets;

/// <summary>
/// Raises (positive <paramref name="Change"/>) or lowers (negative) a budget's allotment. A change rather than a
/// new total, so two admins adjusting the same budget at once both get what they asked for.
/// </summary>
/// <param name="Id">Optional. Becomes the ledger entry's document id; repeating it returns the first result.</param>
public sealed record ChangeAllotment(Guid? Id, Guid BudgetId, decimal Change);

/// <summary>An allotment change as recorded, and the budget's balance right after it.</summary>
public sealed record AllotmentChangeView(Guid Id, Guid BudgetId, decimal Change, BudgetView Budget);

public sealed class ChangeAllotmentHandler(IBudgetsDb db, TimeProvider clock)
{
    public Task<AllotmentChangeView> HandleAsync(ChangeAllotment command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        return db.InTransactionAsync(() => ChangeAsync(command, actor, cancellationToken), cancellationToken);
    }

    private async Task<AllotmentChangeView> ChangeAsync(ChangeAllotment command, Actor actor, CancellationToken cancellationToken)
    {
        if (await db.ReplayAsync<ChangeAllotment, AllotmentChangeView>(command.Id, command, cancellationToken) is { } replay)
        {
            return replay;
        }

        Budget budget = await db.FindBudgetAsync(command.BudgetId, cancellationToken);
        Movement movement = budget.ChangeAllotment(command.Change);
        DateTimeOffset now = clock.GetUtcNow();
        LedgerEntry entry = LedgerEntry.Record(
            budget.Id, command.Id ?? Guid.CreateVersion7(now), LedgerStep.Allot, movement, now, now, actor.Id);

        // Lowering races with grants: the figures just read may already be out of date, so the database checks
        // the floor again in the UPDATE itself. Raising can only help, and is never refused.
        if (command.Change > 0m)
        {
            await db.ApplyAsync(entry, cancellationToken);
        }
        else if (!await db.TryApplyWithinAvailableAsync(entry, "allotment", cancellationToken))
        {
            throw new BusinessRuleException(
                "concurrency.conflict",
                "Funds were reserved or committed on this budget while its allotment was being lowered. Read it again and retry.");
        }

        // Read inside the transaction, after the UPDATE and under its row lock: exactly the figures this change left.
        var changed = new AllotmentChangeView(
            entry.DocumentId, budget.Id, command.Change, BudgetView.From(await db.FindBudgetAsync(budget.Id, cancellationToken)));
        db.Ledger.Add(entry);
        db.Remember(command.Id, command, changed, now);
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
