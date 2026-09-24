using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.EditRequisition;

public sealed record EditRequisitionCommand(Guid RequisitionId, RequisitionDetails Details, Actor Actor)
    : ICommand<RequisitionView>;
