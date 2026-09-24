using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application;

// A bank account change under four eyes: one person proposes, a different one approves. Each is one domain
// call; the runner does the loading, publishing and saving.

public sealed record ProposeBankAccount(Guid SupplierId, string Iban, string Bic, string AccountHolder);

/// <summary>A supplier admin proposes a new account. The one in force stays in force until it is approved.</summary>
public sealed class ProposeBankAccountHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(ProposeBankAccount command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Parsed before the supplier is loaded, so a typing mistake costs no database round trip.
        Iban iban = Iban.Parse(command.Iban);
        Bic bic = Bic.Parse(command.Bic);

        return runner.RunAsync(
            command.SupplierId,
            actor,
            (supplier, now) => supplier.ProposeBankAccount(actor, iban, bic, command.AccountHolder, now),
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
            (supplier, now) => supplier.RejectBankAccount(actor, command.BankAccountId, command.Reason, now),
            cancellationToken);
    }
}
