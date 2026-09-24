using Matchbook.Contracts.Budgets;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IntegrationEvents;

/// <summary>Keeps the local copy of a cost centre at the newest version seen, whatever order versions arrive in.</summary>
public sealed class CostCentreChangedHandler(IRequisitionsDb db, ILogger<CostCentreChangedHandler> logger)
    : IIntegrationEventHandler<CostCentreChanged>
{
    public async Task HandleAsync(CostCentreChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        CostCentre? held = await db.CostCentres.FindAsync([integrationEvent.CostCentreCode], cancellationToken);
        if (held is null)
        {
            db.CostCentres.Add(new CostCentre(
                integrationEvent.CostCentreCode,
                integrationEvent.Version,
                integrationEvent.Name,
                integrationEvent.ManagerId,
                integrationEvent.IsActive));
        }
        else if (!held.Apply(integrationEvent.Version, integrationEvent.Name, integrationEvent.ManagerId, integrationEvent.IsActive))
        {
            logger.StaleCostCentre(integrationEvent.CostCentreCode, integrationEvent.Version, held.Version);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
