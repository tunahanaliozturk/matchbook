using System.ComponentModel.DataAnnotations;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.Api.Features.Budgets;

// Request bodies, validated for shape here (400 with the failing fields). Business rules, such as the allotment
// floor, are the domain's and come back as 409 or 422 with a code.

/// <param name="Id">Optional. Becomes the budget's id; a repeat of this request returns the first result.</param>
public sealed record OpenBudgetRequest(
    Guid? Id,
    [Required, StringLength(CostCentre.CodeMaxLength)] string CostCentreCode,
    [Range(Budget.EarliestFiscalYear, Budget.LatestFiscalYear)] int FiscalYear,
    decimal Allotted);

/// <param name="Id">Optional. Identifies the change in the ledger; a repeat of this request returns the first result.</param>
/// <param name="Change">Positive to raise the allotment, negative to lower it.</param>
public sealed record ChangeAllotmentRequest(Guid? Id, decimal Change);
