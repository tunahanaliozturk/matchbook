using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.CostCentres;

public sealed record CreateCostCentre(string Code, string Name, Guid ManagerId);

public sealed class CreateCostCentreHandler(IBudgetsDb db, IEventPublisher events, TimeProvider clock)
{
    public async Task<CostCentreView> HandleAsync(CreateCostCentre command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CostCentre costCentre = CostCentre.Create(command.Code, command.Name, command.ManagerId);

        // The primary key would refuse a duplicate too; asking first turns that into a 409 with a code.
        if (await db.CostCentres.AnyAsync(c => c.Code == costCentre.Code, cancellationToken))
        {
            throw new BusinessRuleException("cost_centre.already_exists", $"Cost centre {costCentre.Code} already exists.");
        }

        db.CostCentres.Add(costCentre);
        await events.PublishAsync(costCentre.Changed(clock.GetUtcNow()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return CostCentreView.From(costCentre);
    }
}
