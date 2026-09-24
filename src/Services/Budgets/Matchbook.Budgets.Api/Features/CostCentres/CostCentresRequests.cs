using System.ComponentModel.DataAnnotations;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.Api.Features.CostCentres;

// Request bodies, validated for shape here (400 with the failing fields). Business rules, such as the cost centre
// code pattern, are the domain's and come back as 409 or 422 with a code.

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
