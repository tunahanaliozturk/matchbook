using Matchbook.SharedKernel;
using Matchbook.Suppliers.Application.Common;
using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application.Features.Suppliers;

/// <summary>
/// What every command on an existing supplier does around its one domain call: load the supplier, apply the
/// change, publish a snapshot if the version moved, and save both in one <c>SaveChangesAsync</c>, which is what
/// puts the event in the outbox in the same transaction as the change.
/// </summary>
public sealed class SupplierCommandRunner(
    ISuppliersDb db, IEventPublisher events, IFieldProtector protector, SupplierMetrics metrics, TimeProvider clock)
{
    /// <param name="change">The kind of change, as the <c>matchbook.suppliers.changes</c> counter tags it.</param>
    public async Task<SupplierView> RunAsync(
        Guid supplierId,
        Actor actor,
        string change,
        Action<Supplier, DateTimeOffset> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        try
        {
            Supplier supplier = await db.Suppliers.GetAsync(supplierId, cancellationToken);
            long versionBefore = supplier.Version;
            DateTimeOffset now = clock.UtcNowToTheMicrosecond();

            apply(supplier, now);

            if (supplier.Version != versionBefore)
            {
                await events.PublishAsync(SupplierSnapshot.From(supplier, now, protector), cancellationToken);
            }

            // Nothing written means a repeated request that found its work already done: not a new outcome.
            if (await db.SaveChangesAsync(cancellationToken) > 0)
            {
                metrics.Changed(change);
            }

            return SupplierView.For(supplier, actor);
        }
        catch (BusinessRuleException refusal)
        {
            metrics.Refused(refusal.Code);
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            metrics.Refused("concurrency.conflict");
            throw;
        }
    }
}
