using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Budgets;

/// <param name="Id">Optional. Becomes the budget's id; repeating it returns the first result.</param>
public sealed record OpenBudget(Guid? Id, string CostCentreCode, int FiscalYear, decimal Allotted);

public sealed class OpenBudgetHandler(IBudgetsDb db, TimeProvider clock)
{
    public async Task<BudgetView> HandleAsync(OpenBudget command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        if (await db.ReplayAsync<OpenBudget, BudgetView>(command.Id, command, cancellationToken) is { } replay)
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
            command.Id ?? Guid.CreateVersion7(now), costCentre, command.FiscalYear, command.Allotted, actor.Id, now);
        var opened = BudgetView.From(budget);
        db.Budgets.Add(budget);
        db.Ledger.Add(opening);
        db.Remember(command.Id, command, opened, now);
        await db.SaveChangesAsync(cancellationToken);
        return opened;
    }
}
