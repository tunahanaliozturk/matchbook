using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Budgets;

public sealed record OpenBudget(string CostCentreCode, int FiscalYear, decimal Allotted);

public sealed class OpenBudgetHandler(IBudgetsDb db, TimeProvider clock)
{
    public async Task<BudgetView> HandleAsync(OpenBudget command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
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
            Guid.CreateVersion7(now), costCentre, command.FiscalYear, command.Allotted, actor.Id, now);
        db.Budgets.Add(budget);
        db.Ledger.Add(opening);
        await db.SaveChangesAsync(cancellationToken);
        return BudgetView.From(budget);
    }
}
