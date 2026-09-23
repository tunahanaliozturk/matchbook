using System.Diagnostics.CodeAnalysis;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Domain;

/// <summary>Why a reservation or commitment was refused. Mirrors <c>FundsRejectionReason</c> on the wire.</summary>
public enum FundsRefusal
{
    InsufficientFunds,
    NoBudget,
    CostCentreUnavailable,
    DocumentClosed,
}

/// <summary>
/// The refusals that need no arithmetic. What is left after these is a question of available funds, which only
/// the grant itself can answer.
/// </summary>
public static class FundsCheck
{
    public static bool RefusesReservation(
        CostCentre? costCentre, [NotNullWhen(false)] Budget? budget, out FundsRefusal refusal)
    {
        refusal = costCentre is not { IsActive: true } ? FundsRefusal.CostCentreUnavailable : FundsRefusal.NoBudget;
        return costCentre is not { IsActive: true } || budget is null;
    }

    /// <remarks>
    /// The cost centre is not checked: the design refuses a commitment only for money, and an order whose
    /// requisition was reserved while its cost centre was active is allowed through.
    /// </remarks>
    public static bool RefusesCommitment(
        RequisitionReservation? reservation, [NotNullWhen(false)] Budget? budget, out FundsRefusal refusal)
    {
        if (reservation is { Status: ReservationStatus.Released })
        {
            refusal = FundsRefusal.DocumentClosed;
            return true;
        }

        if (budget is null)
        {
            refusal = FundsRefusal.NoBudget;
            return true;
        }

        if (reservation is { Status: ReservationStatus.Held } && reservation.BudgetId != budget.Id)
        {
            throw new BusinessRuleException(
                "funds.budget_mismatch",
                $"Requisition {reservation.RequisitionId} is reserved on budget {reservation.BudgetId}, but its order asks to commit on budget {budget.Id} ({budget.CostCentreCode}, {budget.FiscalYear}).",
                ViolationKind.Invalid);
        }

        refusal = default;
        return false;
    }
}
