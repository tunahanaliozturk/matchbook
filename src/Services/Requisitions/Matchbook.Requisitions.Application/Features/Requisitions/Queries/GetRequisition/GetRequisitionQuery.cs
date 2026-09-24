using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.GetRequisition;

public sealed record GetRequisitionQuery(Guid RequisitionId, Actor Actor) : IQuery<RequisitionView>;
