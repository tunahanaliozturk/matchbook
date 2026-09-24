using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.ApproveRequisition;

public sealed record ApproveRequisitionCommand(Guid RequisitionId, Actor Actor) : ICommand<RequisitionView>;
