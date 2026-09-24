using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.CostCentres.Queries.GetCostCentre;

public sealed record GetCostCentreQuery(string Code) : IQuery<CostCentreView>;
