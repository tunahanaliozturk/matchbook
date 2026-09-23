using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.UseCases;

/// <summary>Requisitions the caller could approve or reject right now, longest waiting first.</summary>
public sealed class ListMyApprovalsHandler(IRequisitionsDb db)
{
    public async Task<Page<RequisitionSummary>> HandleAsync(
        Actor actor,
        long? after,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        int take = Page.Clamp(limit);
        Guid me = actor.Id;
        bool finance = actor.IsIn(Roles.FinanceApprover);
        bool cfo = actor.IsIn(Roles.Cfo);

        // The rule Requisition.Approve enforces, written so the database can run it: pending, not the
        // caller's own, no step of it already decided by the caller, and its current step one the caller may
        // take. If the two ever disagree, the list offers something the approval then refuses.
        IQueryable<Requisition> actionable = db.Requisitions.AsNoTracking().Where(requisition =>
            requisition.Status == RequisitionStatus.PendingApproval
            && requisition.RequesterId != me
            && !requisition.Steps.Any(step => step.DecidedBy == me)
            && requisition.Steps.Any(step =>
                step.Sequence == requisition.CurrentStep
                && ((step.Kind == ApprovalStepKind.Manager && step.ApproverId == me)
                    || (step.Kind == ApprovalStepKind.Finance && finance)
                    || (step.Kind == ApprovalStepKind.Cfo && cfo))));

        if (after is { } cursor)
        {
            actionable = actionable.Where(requisition => requisition.Serial > cursor);
        }

        List<Keyed<RequisitionSummary>> rows = await actionable
            .OrderBy(static requisition => requisition.Serial)
            .Take(take + 1)
            .Select(RequisitionSummary.KeyedBySerial)
            .ToListAsync(cancellationToken);

        return Page.Of(rows, take);
    }
}
