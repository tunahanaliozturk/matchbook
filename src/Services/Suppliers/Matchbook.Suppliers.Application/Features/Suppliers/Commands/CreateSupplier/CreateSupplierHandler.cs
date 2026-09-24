using Matchbook.SharedKernel;
using Matchbook.Suppliers.Application.Common;
using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.CreateSupplier;

/// <summary>
/// A supplier admin records a new supplier as a draft. Nothing is published until it is activated. A tax id
/// another supplier holds is refused by the unique index, which maps to <c>supplier.tax_id_taken</c>, so a
/// request that loses a race gets the same answer as one that arrives second.
/// </summary>
public sealed class CreateSupplierHandler(ISuppliersDb db, SupplierMetrics metrics, TimeProvider clock)
    : ICommandHandler<CreateSupplierCommand, SupplierView>
{
    public async Task<SupplierView> HandleAsync(CreateSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Actor actor = command.Actor;

        try
        {
            SupplierDetails details = SupplierDetails.Create(
                command.LegalName, command.TaxId, command.CountryCode, command.PaymentTermsDays, command.ContactEmail);

            if (command.Id is { } id
                && await db.Suppliers.AsNoTracking().FindAsync(id, cancellationToken) is { } earlier)
            {
                // A retry answers with the supplier as it is now. Once someone has changed it, the retry no
                // longer matches and is refused: the client should read the supplier rather than recreate it.
                return earlier.CreatedBy == actor.Id && earlier.HasDetails(details)
                    ? SupplierView.For(earlier, actor)
                    : throw ClientIds.Reused();
            }

            DateTimeOffset now = clock.UtcNowToTheMicrosecond();
            Supplier supplier = Supplier.Create(actor, command.Id ?? Guid.CreateVersion7(now), details, now);

            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync(cancellationToken);
            metrics.Changed("created");

            return SupplierView.For(supplier, actor);
        }
        catch (BusinessRuleException refusal)
        {
            metrics.Refused(refusal.Code);
            throw;
        }
    }
}
