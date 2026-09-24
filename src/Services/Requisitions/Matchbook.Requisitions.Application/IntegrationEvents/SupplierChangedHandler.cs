using Matchbook.Contracts.Suppliers;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IntegrationEvents;

/// <summary>Keeps the local copy of a supplier at the newest version seen, whatever order versions arrive in.</summary>
public sealed class SupplierChangedHandler(IRequisitionsDb db, ILogger<SupplierChangedHandler> logger)
    : IIntegrationEventHandler<SupplierChanged>
{
    public async Task HandleAsync(SupplierChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // A status this version does not know is not Active: refusing a submission is recoverable, buying
        // from a supplier someone meant to stop is not.
        bool isActive = string.Equals(integrationEvent.Status, SupplierStatus.Active, StringComparison.Ordinal);

        Supplier? held = await db.Suppliers.FindAsync([integrationEvent.SupplierId], cancellationToken);
        if (held is null)
        {
            db.Suppliers.Add(new Supplier(integrationEvent.SupplierId, integrationEvent.Version, integrationEvent.LegalName, isActive));
        }
        else if (!held.Apply(integrationEvent.Version, integrationEvent.LegalName, isActive))
        {
            logger.StaleSupplier(integrationEvent.SupplierId, integrationEvent.Version, held.Version);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
