using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Commands.EditRequisition;

public sealed class EditRequisitionHandler(IRequisitionsDb db, TimeProvider time)
    : ICommandHandler<EditRequisitionCommand, RequisitionView>
{
    public async Task<RequisitionView> HandleAsync(EditRequisitionCommand command, CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(command.RequisitionId, cancellationToken);
        requisition.Edit(command.Actor, command.Details, time.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
