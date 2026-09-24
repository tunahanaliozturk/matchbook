using Matchbook.Requisitions.Domain;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>
/// The shape every event about one of our requisitions shares: load it, let the aggregate decide whether the
/// fact still applies, and save only if it did. The aggregate's refusals are redeliveries and overtaken
/// messages, so they are logged and acknowledged rather than thrown.
/// </summary>
internal static class RequisitionFacts
{
    /// <summary>Returns whether the fact changed the requisition and was saved.</summary>
    public static async Task<bool> RecordAsync(
        this IRequisitionsDb db,
        Guid requisitionId,
        string eventName,
        ILogger logger,
        Func<Requisition, bool> record,
        CancellationToken cancellationToken)
    {
        Requisition? requisition = await db.FindForUpdateAsync(requisitionId, cancellationToken);
        if (requisition is null)
        {
            // Every requisition is committed before its first event leaves the outbox, so this is a message
            // meant for another environment or a database restored from before it. Nothing here can use it.
            logger.UnknownRequisition(eventName, requisitionId);
            return false;
        }

        if (!record(requisition))
        {
            logger.FactIgnored(eventName, requisition.Number, requisition.Status);
            return false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
