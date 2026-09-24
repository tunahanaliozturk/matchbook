using Matchbook.Requisitions.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListRequisitions;

public sealed record ListRequisitionsQuery(long? After, int? Limit, Actor Actor) : IQuery<Page<RequisitionSummary>>;
