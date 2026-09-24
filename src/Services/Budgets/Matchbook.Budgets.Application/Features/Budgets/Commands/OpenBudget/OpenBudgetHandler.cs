using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Features.Budgets.Commands.OpenBudget;

public sealed class OpenBudgetHandler(IBudgetsDb db, TimeProvider clock) : ICommandHandler<OpenBudgetCommand, BudgetView>
{
    public async Task<BudgetView> HandleAsync(OpenBudgetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = new OpenBudget(command.Id, command.CostCentreCode, command.FiscalYear, command.Allotted);
        if (await db.ReplayAsync<OpenBudget, BudgetView>(command.Id, request, cancellationToken) is { } replay)
        {
            return replay;
        }

        CostCentre costCentre = await db.CostCentres.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == command.CostCentreCode, cancellationToken)
            ?? throw NotFound.ForCostCentre(command.CostCentreCode);

        if (await db.Budgets.AnyAsync(
                b => b.CostCentreCode == costCentre.Code && b.FiscalYear == command.FiscalYear, cancellationToken))
        {
            throw new BusinessRuleException(
                "budget.already_exists",
                $"Cost centre {costCentre.Code} already has a budget for {command.FiscalYear}; change its allotment instead.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        (Budget budget, LedgerEntry opening) = Budget.Open(
            command.Id ?? Guid.CreateVersion7(now), costCentre, command.FiscalYear, command.Allotted, command.Actor.Id, now);
        var opened = BudgetView.From(budget);
        db.Budgets.Add(budget);
        db.Ledger.Add(opening);
        db.Remember(command.Id, request, opened, now);
        await db.SaveChangesAsync(cancellationToken);
        return opened;
    }

    /// <summary>
    /// What a repeat is compared with and stored as. The actor is left out, so a repeat gets the first result
    /// whoever sends it, and the type keeps the name stored requests were recorded under before commands had a
    /// suffix, so a retry that spans a deploy still finds its first answer.
    /// </summary>
    private sealed record OpenBudget(Guid? Id, string CostCentreCode, int FiscalYear, decimal Allotted);
}
