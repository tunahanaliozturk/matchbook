using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.UseCases;

/// <summary>
/// Drafts a requisition. Idempotent on the client's id: the same request sent again returns what the first one
/// created, so a client that lost the response to a timeout can retry without raising a second requisition.
/// </summary>
public sealed class CreateRequisitionHandler(IRequisitionsDb db, TimeProvider time)
{
    /// <summary>Every service answers a create that reuses an id for different content with this code.</summary>
    public const string IdReused = "request.id_reused";

    /// <param name="id">The client's id for the requisition, or null to have one made here.</param>
    public async Task<RequisitionView> HandleAsync(
        Actor actor,
        Guid? id,
        RequisitionDetails details,
        CancellationToken cancellationToken)
    {
        if (id is { } chosen && await FindRepeatAsync(actor, chosen, details, cancellationToken) is { } earlier)
        {
            return earlier;
        }

        // A refused draft burns its sequence value. Numbers have gaps anyway (a sequence never gives a value
        // back on rollback), and asking first keeps validation in one place.
        DateTimeOffset now = time.GetUtcNow();
        long serial = await db.NextRequisitionSerialAsync(cancellationToken);
        Requisition requisition = Requisition.Draft(id ?? Guid.CreateVersion7(now), serial, actor, details, now);
        db.Requisitions.Add(requisition);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (id is { } raced)
        {
            // Two requests with one id raced and the other committed first. Answer as if this one had
            // arrived second; if no requisition has the id, the failure was something else.
            RequisitionView? winner = await FindRepeatAsync(actor, raced, details, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        return RequisitionView.From(requisition);
    }

    private async Task<RequisitionView?> FindRepeatAsync(
        Actor actor,
        Guid id,
        RequisitionDetails details,
        CancellationToken cancellationToken)
    {
        Requisition? existing = await db.Requisitions
            .AsNoTracking()
            .Whole()
            .FirstOrDefaultAsync(requisition => requisition.Id == id, cancellationToken);

        // Someone else's requisition under that id is refused the same way, so the answer says nothing about it.
        return existing is null ? null
            : existing.IsRepeatOf(actor, details) ? RequisitionView.From(existing)
            : throw new BusinessRuleException(
                IdReused,
                $"The id {id} was already used for a different request. Send a new id for a new requisition.",
                ViolationKind.Conflict);
    }
}
