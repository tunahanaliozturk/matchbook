using Matchbook.Requisitions.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Approvals.Queries.ListMyApprovals;

public sealed record ListMyApprovalsQuery(long? After, int? Limit, Actor Actor) : IQuery<Page<RequisitionSummary>>;
