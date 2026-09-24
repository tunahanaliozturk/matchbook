using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.SubmitRequisition;

public sealed record SubmitRequisitionCommand(Guid RequisitionId, Actor Actor) : ICommand<RequisitionView>;
