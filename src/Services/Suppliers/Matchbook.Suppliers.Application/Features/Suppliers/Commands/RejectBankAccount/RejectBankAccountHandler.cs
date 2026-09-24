using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.RejectBankAccount;

/// <summary>Either supplier role turns a proposal down with a reason; the account in force is untouched.</summary>
public sealed class RejectBankAccountHandler(SupplierCommandRunner runner)
    : ICommandHandler<RejectBankAccountCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(RejectBankAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "bank_account_rejected",
            (supplier, now) => supplier.RejectBankAccount(command.Actor, command.BankAccountId, command.Reason, now),
            cancellationToken);
    }
}
