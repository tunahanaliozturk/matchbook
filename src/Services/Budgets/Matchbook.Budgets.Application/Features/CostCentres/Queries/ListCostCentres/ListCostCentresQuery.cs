using Matchbook.Budgets.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.CostCentres.Queries.ListCostCentres;

/// <summary>Cost centres in code order. <paramref name="After"/> is the last code of the previous page.</summary>
public sealed record ListCostCentresQuery(string? After = null, int? Limit = null) : IQuery<Page<CostCentreView>>;
