using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ApproveBankAccount;

public sealed record ApproveBankAccountCommand(Guid SupplierId, Guid BankAccountId, Actor Actor) : ICommand<SupplierView>;
