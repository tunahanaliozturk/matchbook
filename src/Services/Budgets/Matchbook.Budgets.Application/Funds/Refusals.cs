using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Funds;

internal static class Refusals
{
    public static string ToReason(this FundsRefusal refusal) => refusal switch
    {
        FundsRefusal.InsufficientFunds => FundsRejectionReason.InsufficientFunds,
        FundsRefusal.NoBudget => FundsRejectionReason.NoBudget,
        FundsRefusal.CostCentreUnavailable => FundsRejectionReason.CostCentreUnavailable,
        FundsRefusal.DocumentClosed => FundsRejectionReason.DocumentClosed,
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a funds refusal."),
    };

    /// <summary>
    /// Available as it is now, for the figure a refusal reports. Read again rather than taken from the row read
    /// before the grant, which is stale by definition when the grant lost a race.
    /// </summary>
    public static Task<decimal> AvailableAsync(this IBudgetsDb db, Guid budgetId, CancellationToken cancellationToken) =>
        db.Budgets
            .Where(b => b.Id == budgetId)
            .Select(b => b.Allotted - b.Reserved - b.Committed - b.Actual)
            .SingleAsync(cancellationToken);
}
