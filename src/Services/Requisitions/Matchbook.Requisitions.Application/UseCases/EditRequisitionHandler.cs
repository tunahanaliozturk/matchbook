using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class EditRequisitionHandler(IRequisitionsDb db, TimeProvider time)
{
    public async Task<RequisitionView> HandleAsync(
        Actor actor,
        Guid requisitionId,
        RequisitionDetails details,
        CancellationToken cancellationToken)
    {
        Requisition requisition = await db.GetForUpdateAsync(requisitionId, cancellationToken);
        requisition.Edit(actor, details, time.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
