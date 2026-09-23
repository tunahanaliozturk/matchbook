using Matchbook.Contracts.Budgets;
using Matchbook.Requisitions.Domain;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>Keeps the local copy of a cost centre at the newest version seen, whatever order versions arrive in.</summary>
public sealed class CostCentreChangedHandler(IRequisitionsDb db, ILogger<CostCentreChangedHandler> logger)
{
    public async Task HandleAsync(CostCentreChanged message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        CostCentre? held = await db.CostCentres.FindAsync([message.CostCentreCode], cancellationToken);
        if (held is null)
        {
            db.CostCentres.Add(new CostCentre(
                message.CostCentreCode, message.Version, message.Name, message.ManagerId, message.IsActive));
        }
        else if (!held.Apply(message.Version, message.Name, message.ManagerId, message.IsActive))
        {
            logger.StaleCostCentre(message.CostCentreCode, message.Version, held.Version);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
