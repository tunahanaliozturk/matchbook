using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;

namespace Matchbook.Budgets.Application.Features.CostCentres;

/// <summary>A cost centre as the API returns it. <see cref="Version"/> is what a change has to name.</summary>
public sealed record CostCentreView(string Code, string Name, Guid ManagerId, bool IsActive, long Version)
{
    internal static CostCentreView From(CostCentre costCentre) =>
        new(costCentre.Code, costCentre.Name, costCentre.ManagerId, costCentre.IsActive, costCentre.Version);
}

internal static class CostCentreMessages
{
    public static CostCentreChanged Changed(this CostCentre costCentre, DateTimeOffset now) =>
        new(costCentre.Code, costCentre.Version, costCentre.Name, costCentre.ManagerId, costCentre.IsActive, now);
}
