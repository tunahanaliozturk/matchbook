using Matchbook.Contracts.Suppliers;
using Matchbook.Requisitions.Domain;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>Keeps the local copy of a supplier at the newest version seen, whatever order versions arrive in.</summary>
public sealed class SupplierChangedHandler(IRequisitionsDb db, ILogger<SupplierChangedHandler> logger)
{
    public async Task HandleAsync(SupplierChanged message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A status this version does not know is not Active: refusing a submission is recoverable, buying
        // from a supplier someone meant to stop is not.
        bool isActive = string.Equals(message.Status, SupplierStatus.Active, StringComparison.Ordinal);

        Supplier? held = await db.Suppliers.FindAsync([message.SupplierId], cancellationToken);
        if (held is null)
        {
            db.Suppliers.Add(new Supplier(message.SupplierId, message.Version, message.LegalName, isActive));
        }
        else if (!held.Apply(message.Version, message.LegalName, isActive))
        {
            logger.StaleSupplier(message.SupplierId, message.Version, held.Version);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
