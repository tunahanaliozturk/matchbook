using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application;

/// <summary>
/// What every command on an existing supplier does around its one domain call: load the supplier, apply the
/// change, publish a snapshot if the version moved, and save both in one <c>SaveChangesAsync</c>, which is what
/// puts the event in the outbox in the same transaction as the change.
/// </summary>
public sealed class SupplierCommandRunner(ISuppliersDb db, IEventPublisher events, IFieldProtector protector, TimeProvider clock)
{
    public async Task<SupplierView> RunAsync(
        Guid supplierId, Actor actor, Action<Supplier, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        Supplier supplier = await db.Suppliers.GetAsync(supplierId, cancellationToken);
        long versionBefore = supplier.Version;
        DateTimeOffset now = clock.GetUtcNow();

        change(supplier, now);

        if (supplier.Version != versionBefore)
        {
            await events.PublishAsync(SupplierSnapshot.From(supplier, now, protector), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return SupplierView.For(supplier, actor);
    }
}
