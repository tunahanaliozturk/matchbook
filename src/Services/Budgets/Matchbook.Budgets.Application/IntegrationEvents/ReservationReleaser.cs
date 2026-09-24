using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.Application.IntegrationEvents;

/// <summary>
/// Gives a requisition's reservation back. Shared by a rejection, a cancellation and the closing of an order that
/// was never committed; it works inside the caller's transaction and leaves saving to the caller.
/// </summary>
public sealed class ReservationReleaser(IBudgetsDb db)
{
    public async Task ReleaseAsync(
        Guid requisitionId, DateTimeOffset occurredAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        RequisitionReservation? reservation = await db.Reservations.FindAsync([requisitionId], cancellationToken);
        if (reservation is null)
        {
            // The release beat the reservation here. Its tombstone makes the late reservation a refusal.
            db.Reservations.Add(RequisitionReservation.Tombstone(requisitionId));
            return;
        }

        if (reservation.Release(occurredAt, now) is { } entry)
        {
            await db.ApplyAsync(entry, cancellationToken);
            db.Ledger.Add(entry);
        }
    }
}
