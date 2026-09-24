using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.CostCentres;

/// <param name="Id">Optional. The client's id for this request; repeating it returns the first result.</param>
public sealed record CreateCostCentre(Guid? Id, string Code, string Name, Guid ManagerId);

public sealed class CreateCostCentreHandler(IBudgetsDb db, IEventPublisher events, TimeProvider clock)
{
    public async Task<CostCentreView> HandleAsync(CreateCostCentre command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.ReplayAsync<CreateCostCentre, CostCentreView>(command.Id, command, cancellationToken) is { } replay)
        {
            return replay;
        }

        CostCentre costCentre = CostCentre.Create(command.Code, command.Name, command.ManagerId);

        // The primary key would refuse a duplicate too; asking first turns that into a 409 with a code.
        if (await db.CostCentres.AnyAsync(c => c.Code == costCentre.Code, cancellationToken))
        {
            throw new BusinessRuleException("cost_centre.already_exists", $"Cost centre {costCentre.Code} already exists.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        var created = CostCentreView.From(costCentre);
        db.CostCentres.Add(costCentre);
        db.Remember(command.Id, command, created, now);
        await events.PublishAsync(costCentre.Changed(now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }
}
