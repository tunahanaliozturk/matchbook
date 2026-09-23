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
/// The refusals that need no arithmetic. What is left after these is a question of available funds, which is
/// answered by the grant itself.
/// </summary>
public static class FundsCheck
{
    public static FundsRefusal? ForReservation(CostCentre? costCentre, Budget? budget) =>
        costCentre is not { IsActive: true } ? FundsRefusal.CostCentreUnavailable
        : budget is null ? FundsRefusal.NoBudget
        : null;

    /// <remarks>
    /// The cost centre is not checked here: the design refuses a commitment only for money, and an order whose
    /// requisition was reserved while the cost centre was active is allowed to go through.
    /// </remarks>
    public static FundsRefusal? ForCommitment(RequisitionReservation? reservation, Budget? budget)
    {
        if (reservation is { Status: ReservationStatus.Released })
        {
            return FundsRefusal.DocumentClosed;
        }

        if (budget is null)
        {
            return FundsRefusal.NoBudget;
        }

        if (reservation is { Status: ReservationStatus.Held } && reservation.BudgetId != budget.Id)
        {
            throw new BusinessRuleException(
                "funds.budget_mismatch",
                $"Requisition {reservation.RequisitionId} is reserved on budget {reservation.BudgetId}, but its order asks to commit on budget {budget.Id} ({budget.CostCentreCode}, {budget.FiscalYear}).",
                ViolationKind.Invalid);
        }

        return null;
    }
}
