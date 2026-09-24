using Matchbook.Contracts.Suppliers;
using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.IntegrationEvents;

/// <summary>Keeps the local copy of a supplier's standing and name, applying a snapshot only when it is newer.</summary>
/// <remarks>
/// The version check is the <c>WHERE</c> of a single <c>UPDATE</c>, so the database decides it under the row
/// lock and two snapshots consumed at once cannot leave the older one in place. A status this service does not
/// know is stored as not active, the cautious reading.
/// </remarks>
public sealed class SupplierChangedHandler(IPurchasingDb db) : IIntegrationEventHandler<SupplierChanged>
{
    public async Task HandleAsync(SupplierChanged message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        bool isActive = string.Equals(message.Status, SupplierStatus.Active, StringComparison.Ordinal);

        int updated = await db.Suppliers
            .Where(supplier => supplier.Id == message.SupplierId && supplier.Version < message.Version)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(supplier => supplier.Version, message.Version)
                    .SetProperty(supplier => supplier.LegalName, message.LegalName)
                    .SetProperty(supplier => supplier.IsActive, isActive),
                cancellationToken);

        if (updated > 0 || await db.Suppliers.AnyAsync(supplier => supplier.Id == message.SupplierId, cancellationToken))
        {
            return;
        }

        // First sight of this supplier. A concurrent first sight fails on the primary key, is retried, and then
        // takes the update path above.
        db.Suppliers.Add(new Supplier(message.SupplierId, message.Version, message.LegalName, isActive));
        await db.SaveChangesAsync(cancellationToken);
    }
}
