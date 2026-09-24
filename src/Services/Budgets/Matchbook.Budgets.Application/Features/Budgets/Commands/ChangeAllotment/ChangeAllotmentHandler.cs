using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Commands.ChangeAllotment;

public sealed class ChangeAllotmentHandler(IBudgetsDb db, TimeProvider clock)
    : ICommandHandler<ChangeAllotmentCommand, AllotmentChangeView>
{
    public Task<AllotmentChangeView> HandleAsync(ChangeAllotmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return db.InTransactionAsync(() => ChangeAsync(command, cancellationToken), cancellationToken);
    }

    private async Task<AllotmentChangeView> ChangeAsync(ChangeAllotmentCommand command, CancellationToken cancellationToken)
    {
        var request = new ChangeAllotment(command.Id, command.BudgetId, command.Change);
        if (await db.ReplayAsync<ChangeAllotment, AllotmentChangeView>(command.Id, request, cancellationToken) is { } replay)
        {
            return replay;
        }

        Budget budget = await db.FindBudgetAsync(command.BudgetId, cancellationToken);
        Movement movement = budget.ChangeAllotment(command.Change);
        DateTimeOffset now = clock.GetUtcNow();
        LedgerEntry entry = LedgerEntry.Record(
            budget.Id, command.Id ?? Guid.CreateVersion7(now), LedgerStep.Allot, movement, now, now, command.Actor.Id);

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
        db.Remember(command.Id, request, changed, now);
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    /// <summary>
    /// What a repeat is compared with and stored as. The actor is left out, so a repeat gets the first result
    /// whoever sends it, and the type keeps the name stored requests were recorded under before commands had a
    /// suffix, so a retry that spans a deploy still finds its first answer.
    /// </summary>
    private sealed record ChangeAllotment(Guid? Id, Guid BudgetId, decimal Change);
}
