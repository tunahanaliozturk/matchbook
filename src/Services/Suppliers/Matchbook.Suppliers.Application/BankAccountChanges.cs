using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application;

// A bank account change under four eyes: one person proposes, a different one approves. Each is one domain
// call; the runner does the loading, publishing and saving.

/// <param name="Id">Optional. Sending the same id again returns the supplier instead of proposing twice.</param>
public sealed record ProposeBankAccount(Guid SupplierId, Guid? Id, string Iban, string Bic, string AccountHolder);

/// <summary>A supplier admin proposes a new account. The one in force stays in force until it is approved.</summary>
public sealed class ProposeBankAccountHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(ProposeBankAccount command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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

public sealed record ApproveBankAccount(Guid SupplierId, Guid BankAccountId);

/// <summary>A supplier approver who did not propose it puts the account in force, which is published.</summary>
public sealed class ApproveBankAccountHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(ApproveBankAccount command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            actor,
            "bank_account_approved",
            (supplier, now) => supplier.ApproveBankAccount(actor, command.BankAccountId, now),
            cancellationToken);
    }
}

public sealed record RejectBankAccount(Guid SupplierId, Guid BankAccountId, string Reason);

/// <summary>Either supplier role turns a proposal down with a reason; the account in force is untouched.</summary>
public sealed class RejectBankAccountHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(RejectBankAccount command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            actor,
            "bank_account_rejected",
            (supplier, now) => supplier.RejectBankAccount(actor, command.BankAccountId, command.Reason, now),
            cancellationToken);
    }
}
