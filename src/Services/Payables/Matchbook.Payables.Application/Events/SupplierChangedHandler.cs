using Matchbook.Contracts.Suppliers;
using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Events;

/// <summary>Keeps the local copy of a supplier, applying a snapshot only when it is newer than the one held.</summary>
public sealed class SupplierChangedHandler(IPayablesDb db)
{
    public async Task HandleAsync(SupplierChanged message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        SupplierAccount? account = message.BankAccount is { } verified
            ? new SupplierAccount(
                verified.AccountVersion,
                Iban.Parse(verified.Iban),
                Bic.Parse(verified.Bic),
                verified.AccountHolder)
            : null;
        var snapshot = new SupplierSnapshot(
            message.Version,
            message.LegalName,
            string.Equals(message.Status, SupplierStatus.Active, StringComparison.Ordinal),
            message.PaymentTermsDays,
            account,
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
}
