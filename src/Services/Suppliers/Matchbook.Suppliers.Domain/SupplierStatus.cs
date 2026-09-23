namespace Matchbook.Suppliers.Domain;

/// <summary>Where a supplier is in its life. Only <see cref="Active"/> suppliers can be ordered from or paid.</summary>
public enum SupplierStatus
{
    Draft,
    PendingActivation,
    Active,
    Blocked,
}
