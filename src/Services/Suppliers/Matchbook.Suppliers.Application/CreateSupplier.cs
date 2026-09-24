using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application;

public sealed record CreateSupplier(
    string LegalName, string TaxId, string CountryCode, int PaymentTermsDays, string ContactEmail);

/// <summary>A supplier admin records a new supplier as a draft. Nothing is published until it is activated.</summary>
public sealed class CreateSupplierHandler(ISuppliersDb db, TimeProvider clock)
{
    public async Task<SupplierView> HandleAsync(CreateSupplier command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        SupplierDetails details = SupplierDetails.Create(
            command.LegalName, command.TaxId, command.CountryCode, command.PaymentTermsDays, command.ContactEmail);
        Supplier supplier = Supplier.Create(actor, details, clock.GetUtcNow());

        await db.EnsureTaxIdIsFreeAsync(supplier.TaxId, supplier.Id, cancellationToken);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);

        return SupplierView.For(supplier, actor);
    }
}
