using Matchbook.Contracts.Suppliers;
using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.IntegrationEvents;

/// <summary>Keeps the local copy of a supplier, applying a snapshot only when it is newer than the one held.</summary>
public sealed class SupplierChangedHandler(IPayablesDb db, IFieldProtector protector) : IIntegrationEventHandler<SupplierChanged>
{
    public async Task HandleAsync(SupplierChanged message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var snapshot = new SupplierSnapshot(
            message.Version,
            message.LegalName,
            string.Equals(message.Status, SupplierStatus.Active, StringComparison.Ordinal),
            message.PaymentTermsDays,
            message.BankAccount is { } verified ? Account(verified) : null,
            message.OccurredAt.ToUniversalTime());

        Supplier? supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == message.SupplierId, cancellationToken);
        if (supplier is null)
        {
            db.Suppliers.Add(Supplier.From(message.SupplierId, snapshot));
        }
        else if (!supplier.Apply(snapshot))
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Decrypts the account number only to check it, and keeps the ciphertext Suppliers sent. Both services hold the
    /// same key, so there is nothing to gain from encrypting it again, and the plain number never reaches a column.
    /// The last four characters come from the number itself rather than from the event's display field.
    /// </summary>
    private SupplierAccount Account(VerifiedBankAccount verified)
    {
        Iban iban = Iban.Parse(protector.Unprotect(verified.ProtectedIban));
        return new SupplierAccount(verified.AccountVersion, verified.ProtectedIban, iban.LastFour, Bic.Parse(verified.Bic), verified.AccountHolder);
    }
}
