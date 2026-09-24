using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.RejectRequisition;

public sealed record RejectRequisitionCommand(Guid RequisitionId, string Reason, Actor Actor) : ICommand<RequisitionView>;
