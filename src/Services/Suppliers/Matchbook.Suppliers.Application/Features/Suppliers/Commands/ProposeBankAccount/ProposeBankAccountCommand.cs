using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ProposeBankAccount;

/// <param name="Id">Optional. Sending the same id again returns the supplier instead of proposing twice.</param>
public sealed record ProposeBankAccountCommand(
    Guid SupplierId, Guid? Id, string Iban, string Bic, string AccountHolder, Actor Actor) : ICommand<SupplierView>;
