using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ApproveBankAccount;

/// <summary>A supplier approver who did not propose it puts the account in force, which is published.</summary>
public sealed class ApproveBankAccountHandler(SupplierCommandRunner runner)
    : ICommandHandler<ApproveBankAccountCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(ApproveBankAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "bank_account_approved",
            (supplier, now) => supplier.ApproveBankAccount(command.Actor, command.BankAccountId, now),
            cancellationToken);
    }
}
