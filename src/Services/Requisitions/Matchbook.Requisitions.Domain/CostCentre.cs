namespace Matchbook.Requisitions.Domain;

/// <summary>
/// This service's copy of a cost centre, kept from <c>CostCentreChanged</c>. Budgets owns the real one; the copy
/// exists so that submitting and routing never wait on another service.
/// </summary>
public sealed class CostCentre
{
    // Budgets' format is ^[A-Z]{2,5}-[A-Z0-9]{2,12}$, so no real code is longer.
    public const int MaxCodeLength = 18;

    public CostCentre(string code, long version, string name, Guid managerId, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Version = version;
        Name = name;
        ManagerId = managerId;
        IsActive = isActive;
    }

    public string Code { get; private set; }

    public long Version { get; private set; }

    public string Name { get; private set; }

    public Guid ManagerId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Takes a snapshot only when it is newer than the one held, so a redelivered or overtaken message changes
    /// nothing. Returns whether it did.
    /// </summary>
    public bool Apply(long version, string name, Guid managerId, bool isActive)
    {
        if (version <= Version)
        {
            return false;
        }

        Version = version;
        Name = name;
        ManagerId = managerId;
        IsActive = isActive;
        return true;
    }
}
