using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.Common;

internal static class RequisitionLoading
{
    /// <summary>The whole aggregate: lines, route and timeline, each read by its own query.</summary>
    public static IQueryable<Requisition> Whole(this IQueryable<Requisition> requisitions) =>
        requisitions
            .Include(static requisition => requisition.Lines)
            .Include(static requisition => requisition.Steps)
            .Include(static requisition => requisition.Timeline)
            .AsSplitQuery();

    /// <summary>The tracked aggregate, or null when there is no such requisition.</summary>
    public static Task<Requisition?> FindForUpdateAsync(this IRequisitionsDb db, Guid id, CancellationToken cancellationToken) =>
        db.Requisitions.Whole().FirstOrDefaultAsync(requisition => requisition.Id == id, cancellationToken);

    /// <summary>The tracked aggregate, for a command: a missing requisition is the caller's 404.</summary>
    public static async Task<Requisition> GetForUpdateAsync(this IRequisitionsDb db, Guid id, CancellationToken cancellationToken) =>
        await db.FindForUpdateAsync(id, cancellationToken) ?? throw NotFound(id);

    public static BusinessRuleException NotFound(Guid id) =>
        new(RequisitionCodes.NotFound, $"There is no requisition {id}.", ViolationKind.NotFound);
}
