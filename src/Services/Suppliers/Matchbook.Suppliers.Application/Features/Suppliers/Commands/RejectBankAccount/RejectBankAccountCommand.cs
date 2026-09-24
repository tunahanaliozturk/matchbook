using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.RejectBankAccount;

public sealed record RejectBankAccountCommand(Guid SupplierId, Guid BankAccountId, string Reason, Actor Actor)
    : ICommand<SupplierView>;
