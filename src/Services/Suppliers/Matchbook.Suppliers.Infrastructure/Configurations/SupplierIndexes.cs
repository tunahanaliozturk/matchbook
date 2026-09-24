namespace Matchbook.Suppliers.Infrastructure.Configurations;

/// <summary>
/// The unique constraints a request can run into, by name. The configurations create them under these names and
/// the service maps each one to the code a client sees, so renaming one in a migration breaks a test rather than
/// silently turning a 409 with a code into a generic one.
/// </summary>
internal static class SupplierIndexes
{
    public const string TaxId = "ux_suppliers_tax_id";

    public const string OnePendingBankAccount = "ux_bank_accounts_one_pending_per_supplier";

    // A client-chosen id that is already taken, by a request that raced its own retry or by another client.
    public const string SupplierKey = "pk_suppliers";

    public const string BankAccountKey = "pk_bank_accounts";
}
