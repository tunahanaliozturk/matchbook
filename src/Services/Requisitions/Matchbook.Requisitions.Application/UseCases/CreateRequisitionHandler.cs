using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.UseCases;

public sealed class CreateRequisitionHandler(IRequisitionsDb db, TimeProvider time)
{
    public async Task<RequisitionView> HandleAsync(Actor actor, RequisitionDetails details, CancellationToken cancellationToken)
    {
        // A refused draft burns its sequence value. Numbers have gaps anyway (a sequence never gives a value
        // back on rollback), and asking first keeps validation in one place.
        long serial = await db.NextRequisitionSerialAsync(cancellationToken);
        Requisition requisition = Requisition.Draft(serial, actor, details, time.GetUtcNow());

        db.Requisitions.Add(requisition);
        await db.SaveChangesAsync(cancellationToken);
        return RequisitionView.From(requisition);
    }
}
