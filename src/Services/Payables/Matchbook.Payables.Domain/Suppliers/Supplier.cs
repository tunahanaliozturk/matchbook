namespace Matchbook.Payables.Domain.Suppliers;

/// <summary>
/// The local copy of a supplier, kept from Suppliers' state snapshots. A snapshot is applied only when its version is
/// newer than the one held, so redelivery and reordering both leave the latest state in place.
/// </summary>
public sealed class Supplier
{
    private Supplier(Guid id, long version, string legalName, bool isActive, int paymentTermsDays, DateTimeOffset changedAt)
    {
        Id = id;
        Version = version;
        LegalName = legalName;
        IsActive = isActive;
        PaymentTermsDays = paymentTermsDays;
        ChangedAt = changedAt;
    }

    public Guid Id { get; private set; }

    public long Version { get; private set; }

    public string LegalName { get; private set; }

    /// <summary>True only for a status Payables recognises as active; anything else, unknown values included, is not.</summary>
    public bool IsActive { get; private set; }

    public int PaymentTermsDays { get; private set; }

    /// <summary>The bank account a second person verified, or null when there is none yet.</summary>
    public SupplierAccount? Account { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>A payment run may pay this supplier: active, with a verified account.</summary>
    public bool CanBePaid => IsActive && Account is not null;

    public static Supplier From(Guid id, SupplierSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var supplier = new Supplier(
            id,
            snapshot.Version,
            snapshot.LegalName,
            snapshot.IsActive,
            snapshot.PaymentTermsDays,
            snapshot.ChangedAt);
        supplier.Account = snapshot.Account;
        return supplier;
    }

    /// <summary>Applies a newer snapshot. Returns false, changing nothing, for one that is not newer.</summary>
    public bool Apply(SupplierSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Version <= Version)
        {
            return false;
        }

        Version = snapshot.Version;
        LegalName = snapshot.LegalName;
        IsActive = snapshot.IsActive;
        PaymentTermsDays = snapshot.PaymentTermsDays;
        Account = snapshot.Account;
        ChangedAt = snapshot.ChangedAt;
        return true;
    }
}

/// <summary>A supplier's state as one <c>SupplierChanged</c> described it.</summary>
public sealed record SupplierSnapshot(
    long Version,
    string LegalName,
    bool IsActive,
    int PaymentTermsDays,
    SupplierAccount? Account,
    DateTimeOffset ChangedAt);

/// <summary>
/// A verified bank account. <see cref="AccountVersion"/> rises with every approved change, which is how a payment
/// run tells that the account it was drafted against is no longer the current one.
/// </summary>
public sealed record SupplierAccount(int AccountVersion, Iban Iban, Bic Bic, string AccountHolder);
