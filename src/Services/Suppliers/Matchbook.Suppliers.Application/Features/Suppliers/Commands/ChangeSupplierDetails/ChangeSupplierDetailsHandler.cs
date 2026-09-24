using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ChangeSupplierDetails;

/// <summary>
/// A supplier admin corrects the details. A new name, country or terms is published once active. A tax id
/// another supplier holds is refused by the unique index, as on create.
/// </summary>
public sealed class ChangeSupplierDetailsHandler(SupplierCommandRunner runner)
    : ICommandHandler<ChangeSupplierDetailsCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(ChangeSupplierDetailsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "details_changed",
            (supplier, _) => supplier.ChangeDetails(command.Actor, SupplierDetails.Create(
                command.LegalName,
                command.TaxId,
                command.CountryCode,
                command.PaymentTermsDays,
                command.ContactEmail)),
            cancellationToken);
    }
}
