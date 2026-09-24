using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ChangeSupplierDetails;

public sealed record ChangeSupplierDetailsCommand(
    Guid SupplierId,
    string LegalName,
    string TaxId,
    string CountryCode,
    int PaymentTermsDays,
    string ContactEmail,
    Actor Actor) : ICommand<SupplierView>;
