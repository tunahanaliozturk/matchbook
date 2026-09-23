using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application;

public sealed record ChangeSupplierDetails(
    Guid SupplierId,
    string LegalName,
    string TaxId,
    string CountryCode,
    int PaymentTermsDays,
    string ContactEmail);

/// <summary>A supplier admin corrects the details. A new name, country or terms is published once active.</summary>
public sealed class ChangeSupplierDetailsHandler(ISuppliersDb db, SupplierCommandRunner runner)
{
    public async Task<SupplierView> HandleAsync(
        ChangeSupplierDetails command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        SupplierDetails details = SupplierDetails.Create(
            command.LegalName, command.TaxId, command.CountryCode, command.PaymentTermsDays, command.ContactEmail);
        await db.EnsureTaxIdIsFreeAsync(details.TaxId, command.SupplierId, cancellationToken);

        return await runner.RunAsync(
            command.SupplierId, actor, (supplier, _) => supplier.ChangeDetails(actor, details), cancellationToken);
    }
}
