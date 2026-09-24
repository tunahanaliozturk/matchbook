using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.CreateRequisition;

/// <param name="Id">The client's id for the requisition, or null to have one made here.</param>
public sealed record CreateRequisitionCommand(Guid? Id, RequisitionDetails Details, Actor Actor) : ICommand<RequisitionView>;
