using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Features.CostCentres.Commands.CreateCostCentre;

public sealed class CreateCostCentreHandler(IBudgetsDb db, IEventPublisher events, TimeProvider clock)
    : ICommandHandler<CreateCostCentreCommand, CostCentreView>
{
    public async Task<CostCentreView> HandleAsync(CreateCostCentreCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = new CreateCostCentre(command.Id, command.Code, command.Name, command.ManagerId);
        if (await db.ReplayAsync<CreateCostCentre, CostCentreView>(command.Id, request, cancellationToken) is { } replay)
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
        db.Remember(command.Id, request, created, now);
        await events.PublishAsync(costCentre.Changed(now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    /// <summary>
    /// What a repeat is compared with and stored as, under the name stored requests were recorded under before
    /// commands had a suffix, so a retry that spans a deploy still finds its first answer.
    /// </summary>
    private sealed record CreateCostCentre(Guid? Id, string Code, string Name, Guid ManagerId);
}
