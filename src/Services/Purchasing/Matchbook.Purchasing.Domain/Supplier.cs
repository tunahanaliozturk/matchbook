namespace Matchbook.Purchasing.Domain;

/// <summary>
/// Purchasing's copy of a supplier, kept from <c>SupplierChanged</c>: whether it may be ordered from, and a name
/// to show on its orders. The rest of the supplier belongs to the Suppliers service.
/// </summary>
/// <remarks>
/// A snapshot is applied only when its version is newer than the one held. That rule runs as one conditional
/// update in the database rather than here, because two snapshots for the same supplier can be consumed at the
/// same moment and a read-then-write would let the older one land last.
/// </remarks>
public sealed class Supplier(Guid id, long version, string? legalName, bool isActive)
{
    public Guid Id { get; private set; } = id;

    public long Version { get; private set; } = version;

    /// <summary>
    /// Null only for a copy stored before names were kept, until the supplier's next snapshot. Every snapshot
    /// carries one.
    /// </summary>
    public string? LegalName { get; private set; } = legalName;

    public bool IsActive { get; private set; } = isActive;
}
