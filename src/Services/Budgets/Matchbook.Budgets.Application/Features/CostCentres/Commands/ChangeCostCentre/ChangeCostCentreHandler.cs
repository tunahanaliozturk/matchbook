using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.CostCentres.Commands.ChangeCostCentre;

public sealed class ChangeCostCentreHandler(IBudgetsDb db, IEventPublisher events, TimeProvider clock)
    : ICommandHandler<ChangeCostCentreCommand, CostCentreView>
{
    public async Task<CostCentreView> HandleAsync(ChangeCostCentreCommand command, CancellationToken cancellationToken)
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
