using System.ComponentModel.DataAnnotations;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.Api;

// Request bodies, validated for shape here (400 with the failing fields). Business rules, such as the cost centre
// code pattern or the allotment floor, are the domain's and come back as 409 or 422 with a code.

/// <param name="Id">Optional. Send one and a repeat of this request returns the first result instead of a second cost centre.</param>
/// <param name="Code">Two to five capital letters, a hyphen, then two to twelve capital letters or digits: ENG-PLATFORM.</param>
public sealed record CreateCostCentreRequest(
    Guid? Id,
    [Required, StringLength(CostCentre.CodeMaxLength)] string Code,
    [Required, StringLength(CostCentre.NameMaxLength)] string Name,
    Guid ManagerId);

/// <param name="Version">The version the change is made against, as last read. A newer one on the server is a 409.</param>
public sealed record ChangeCostCentreRequest(
    long Version,
    [Required, StringLength(CostCentre.NameMaxLength)] string Name,
    Guid ManagerId,
    bool IsActive);

/// <param name="Id">Optional. Becomes the budget's id; a repeat of this request returns the first result.</param>
public sealed record OpenBudgetRequest(
    Guid? Id,
    [Required, StringLength(CostCentre.CodeMaxLength)] string CostCentreCode,
    [Range(Budget.EarliestFiscalYear, Budget.LatestFiscalYear)] int FiscalYear,
    decimal Allotted);

/// <param name="Id">Optional. Identifies the change in the ledger; a repeat of this request returns the first result.</param>
/// <param name="Change">Positive to raise the allotment, negative to lower it.</param>
public sealed record ChangeAllotmentRequest(Guid? Id, decimal Change);
