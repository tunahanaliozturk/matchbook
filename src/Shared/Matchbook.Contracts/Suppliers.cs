namespace Matchbook.Contracts.Suppliers;

/// <summary>
/// The state of a supplier after any change another service acts on: activation, blocking, unblocking, a new
/// verified bank account, a changed name or payment terms. Published from the supplier's first activation on.
/// </summary>
/// <remarks>
/// A state snapshot rather than a set of fine-grained events, because every consumer keeps a local copy and
/// wants the same thing: the latest state. Apply a message only when <see cref="Version"/> is higher than the
/// version held, and redelivery and reordering are both harmless.
/// </remarks>
public sealed record SupplierChanged(
    Guid SupplierId,
    long Version,
    string LegalName,
    string CountryCode,
    string Status,
    int PaymentTermsDays,
    VerifiedBankAccount? BankAccount,
    DateTimeOffset OccurredAt);

/// <summary>
/// A bank account a second person approved. <see cref="AccountVersion"/> rises with every approved change, so a
/// payment run can tell whether the account it was drafted against is still the current one.
/// </summary>
public sealed record VerifiedBankAccount(int AccountVersion, string Iban, string Bic, string AccountHolder);

/// <summary>Values of <see cref="SupplierChanged.Status"/>.</summary>
public static class SupplierStatus
{
    public const string Active = "Active";
    public const string Blocked = "Blocked";
}
