namespace Matchbook.Requisitions.Domain;

/// <summary>
/// This service's copy of a supplier, kept from <c>SupplierChanged</c>. Only what submitting needs: whether the
/// supplier may be bought from, and a name to show.
/// </summary>
public sealed class Supplier
{
    public Supplier(Guid id, long version, string legalName, bool isActive)
    {
        Id = id;
        Version = version;
        LegalName = legalName;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    public long Version { get; private set; }

    public string LegalName { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Takes a snapshot only when it is newer than the one held. Returns whether it did.</summary>
    public bool Apply(long version, string legalName, bool isActive)
    {
        if (version <= Version)
        {
            return false;
        }

        Version = version;
        LegalName = legalName;
        IsActive = isActive;
        return true;
    }
}
