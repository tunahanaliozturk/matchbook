using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.CostCentres;

/// <summary>
/// A change to a cost centre, made against the <paramref name="ExpectedVersion"/> the editor saw, so a second
/// editor working from the same version gets a 409 instead of silently overwriting the first.
/// </summary>
public sealed record ChangeCostCentre(string Code, long ExpectedVersion, string Name, Guid ManagerId, bool IsActive);

public sealed class ChangeCostCentreHandler(IBudgetsDb db, IEventPublisher events, TimeProvider clock)
{
    public async Task<CostCentreView> HandleAsync(ChangeCostCentre command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CostCentre costCentre = await db.CostCentres.FindAsync([command.Code], cancellationToken)
            ?? throw NotFound.ForCostCentre(command.Code);

        // The version check covers the time the editor spent on the form; the row's xmin token covers the
        // milliseconds between this read and the save.
        if (costCentre.Change(command.ExpectedVersion, command.Name, command.ManagerId, command.IsActive))
        {
            await events.PublishAsync(costCentre.Changed(clock.GetUtcNow()), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return CostCentreView.From(costCentre);
    }
}
