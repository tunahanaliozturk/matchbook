using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.CostCentres.Commands.ChangeCostCentre;

/// <summary>
/// A change to a cost centre, made against the <paramref name="ExpectedVersion"/> the editor saw, so a second
/// editor working from the same version gets a 409 instead of silently overwriting the first.
/// </summary>
public sealed record ChangeCostCentreCommand(string Code, long ExpectedVersion, string Name, Guid ManagerId, bool IsActive)
    : ICommand<CostCentreView>;
