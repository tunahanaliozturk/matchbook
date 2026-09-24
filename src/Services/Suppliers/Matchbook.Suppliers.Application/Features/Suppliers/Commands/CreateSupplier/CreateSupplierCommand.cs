using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.CreateSupplier;

/// <param name="Id">Optional. Sending the same id again returns the supplier it created instead of a second one.</param>
public sealed record CreateSupplierCommand(
    Guid? Id, string LegalName, string TaxId, string CountryCode, int PaymentTermsDays, string ContactEmail, Actor Actor)
    : ICommand<SupplierView>;
