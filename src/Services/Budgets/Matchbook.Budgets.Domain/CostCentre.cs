using System.Text.RegularExpressions;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Domain;

/// <summary>
/// A part of the organisation that holds budgets. Requisitions keeps a copy of it from <c>CostCentreChanged</c>
/// and applies a message only when its version is newer, so every create and every real change moves
/// <see cref="Version"/> on by exactly one.
/// </summary>
public sealed partial class CostCentre
{
    public const int CodeMaxLength = 18;
    public const int NameMaxLength = 200;

    private CostCentre(string code, string name, Guid managerId, bool isActive, long version)
    {
        Code = code;
        Name = name;
        ManagerId = managerId;
        IsActive = isActive;
        Version = version;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public Guid ManagerId { get; private set; }

    public bool IsActive { get; private set; }

    public long Version { get; private set; }

    public static CostCentre Create(string code, string name, Guid managerId)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (!CodePattern().IsMatch(code))
        {
            throw new BusinessRuleException(
                "cost_centre.code_invalid",
                $"'{code}' is not a cost centre code: two to five capital letters, a hyphen, then two to twelve capital letters or digits.",
                ViolationKind.Invalid);
        }

        return new CostCentre(code, ValidName(name), ValidManager(managerId), isActive: true, version: 1);
    }

    /// <summary>
    /// Applies an edit made against <paramref name="expectedVersion"/>. Returns false, and leaves the version
    /// alone, when the edit changes nothing, so saving the same form twice publishes one event, not two.
    /// </summary>
    public bool Change(long expectedVersion, string name, Guid managerId, bool isActive)
    {
        if (expectedVersion != Version)
        {
            throw new BusinessRuleException(
                "concurrency.conflict",
                $"Cost centre {Code} is at version {Version}, not {expectedVersion}. Read it again before changing it.");
        }

        name = ValidName(name);
        ValidManager(managerId);
        if (name == Name && managerId == ManagerId && isActive == IsActive)
        {
            return false;
        }

        Name = name;
        ManagerId = managerId;
        IsActive = isActive;
        Version++;
        return true;
    }

    private static string ValidName(string name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= NameMaxLength
            ? trimmed
            : throw new BusinessRuleException(
                "cost_centre.name_invalid",
                $"A cost centre needs a name of 1 to {NameMaxLength} characters.",
                ViolationKind.Invalid);
    }

    private static Guid ValidManager(Guid managerId) =>
        managerId != Guid.Empty
            ? managerId
            : throw new BusinessRuleException(
                "cost_centre.manager_required",
                "A cost centre needs a manager: the first approval of every requisition goes to them.",
                ViolationKind.Invalid);

    // \z rather than $: in .NET, $ also matches before a trailing newline, which would let "ENG-OPS\n" through.
    [GeneratedRegex(@"^[A-Z]{2,5}-[A-Z0-9]{2,12}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
