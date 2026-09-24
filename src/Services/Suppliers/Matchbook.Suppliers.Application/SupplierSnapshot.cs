using Matchbook.Suppliers.Domain;
using Contract = Matchbook.Contracts.Suppliers;

namespace Matchbook.Suppliers.Application;

internal static class SupplierSnapshot
{
    /// <summary>
    /// The state other services keep a copy of. It carries the full IBAN, because Payables writes it into the
    /// payment file; Payables encrypts it at rest like this service does.
    /// </summary>
    public static Contract.SupplierChanged From(Supplier supplier, DateTimeOffset now)
    {
        BankAccount? account = supplier.VerifiedAccount;

        return new Contract.SupplierChanged(
            supplier.Id,
            supplier.Version,
            supplier.LegalName,
            supplier.Country.Value,
            supplier.Status switch
            {
                SupplierStatus.Active => Contract.SupplierStatus.Active,
                SupplierStatus.Blocked => Contract.SupplierStatus.Blocked,

                // Unreachable while the version only moves after activation, and the contract has no word for it.
                _ => throw new InvalidOperationException($"A {supplier.Status} supplier is not published."),
            },
            supplier.PaymentTermsDays,
            account is null
                ? null
                : new Contract.VerifiedBankAccount(
                    supplier.AccountVersion, account.Iban.Value, account.Bic.Value, account.AccountHolder),
            now);
    }
}
