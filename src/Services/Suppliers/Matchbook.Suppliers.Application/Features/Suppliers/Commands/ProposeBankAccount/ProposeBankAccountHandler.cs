using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ProposeBankAccount;

// A bank account change under four eyes: one person proposes here, a different one approves in
// ApproveBankAccount. The runner does the loading, publishing and saving.

/// <summary>A supplier admin proposes a new account. The one in force stays in force until it is approved.</summary>
public sealed class ProposeBankAccountHandler(SupplierCommandRunner runner)
    : ICommandHandler<ProposeBankAccountCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(ProposeBankAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Actor actor = command.Actor;

        return runner.RunAsync(
            command.SupplierId,
            actor,
            "bank_account_proposed",
            (supplier, now) =>
            {
                // Parsed inside the runner so a mistyped IBAN is counted with every other refusal.
                Iban iban = Iban.Parse(command.Iban);
                Bic bic = Bic.Parse(command.Bic);

                if (command.Id is { } id && supplier.BankAccounts.FirstOrDefault(account => account.Id == id) is { } earlier)
                {
                    // A retry finds its proposal already recorded, changes nothing, and answers as the first did.
                    if (earlier.ProposedBy != actor.Id || !earlier.HasSameDetailsAs(iban, bic, command.AccountHolder))
                    {
                        throw ClientIds.Reused();
                    }

                    return;
                }

                supplier.ProposeBankAccount(
                    actor, command.Id ?? Guid.CreateVersion7(now), iban, bic, command.AccountHolder, now);
            },
            cancellationToken);
    }
}
