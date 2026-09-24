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

/// <summary>
/// A supplier admin corrects the details. A new name, country or terms is published once active. A tax id
/// another supplier holds is refused by the unique index, as on create.
/// </summary>
public sealed class ChangeSupplierDetailsHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(ChangeSupplierDetails command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return runner.RunAsync(
            command.SupplierId,
            actor,
            "details_changed",
            (supplier, _) => supplier.ChangeDetails(actor, SupplierDetails.Create(
                command.LegalName,
                command.TaxId,
                command.CountryCode,
                command.PaymentTermsDays,
                command.ContactEmail)),
            cancellationToken);
    }
}
