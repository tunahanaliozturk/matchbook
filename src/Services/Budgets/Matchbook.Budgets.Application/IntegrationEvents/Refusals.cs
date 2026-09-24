using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;

namespace Matchbook.Budgets.Application.IntegrationEvents;

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
}
