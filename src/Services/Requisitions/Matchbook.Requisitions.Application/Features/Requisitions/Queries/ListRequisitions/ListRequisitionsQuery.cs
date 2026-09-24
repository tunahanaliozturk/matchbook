using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListRequisitions;

/// <param name="Statuses">Only requisitions in one of these; null or empty for every status.</param>
public sealed record ListRequisitionsQuery(IReadOnlyList<RequisitionStatus>? Statuses, long? After, int? Limit, Actor Actor)
    : IQuery<Page<RequisitionSummary>>;
