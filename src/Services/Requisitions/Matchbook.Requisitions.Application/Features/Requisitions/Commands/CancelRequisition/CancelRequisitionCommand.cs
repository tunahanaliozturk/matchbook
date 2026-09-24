using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.CancelRequisition;

public sealed record CancelRequisitionCommand(Guid RequisitionId, Actor Actor) : ICommand<RequisitionView>;
