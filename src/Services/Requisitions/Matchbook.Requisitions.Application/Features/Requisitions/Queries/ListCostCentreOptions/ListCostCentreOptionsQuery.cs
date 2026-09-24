using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListCostCentreOptions;

public sealed record ListCostCentreOptionsQuery(Actor Actor) : IQuery<IReadOnlyList<CostCentreOption>>;

/// <summary>A cost centre as the requisition form offers it.</summary>
public sealed record CostCentreOption(string Code, string Name);
