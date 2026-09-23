using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Domain;

/// <summary>
/// A cost centre's money for one fiscal year, as four figures:
/// available = allotted - reserved - committed - actual.
/// </summary>
/// <remarks>
/// In the running service the figures change only through one set-based UPDATE per ledger entry, so that a grant
/// and the check that allows it are one statement under the row lock. <see cref="CanAfford"/> is the rule that
/// statement's WHERE clause evaluates; <see cref="Apply"/> is the same arithmetic for code that holds a budget in
/// memory, and enforces the invariants the database's check constraints enforce.
/// </remarks>
public sealed class Budget
{
    public const int EarliestFiscalYear = 2000;
    public const int LatestFiscalYear = 2100;

    private Budget(
        Guid id,
        string costCentreCode,
        int fiscalYear,
        decimal allotted,
        decimal reserved,
        decimal committed,
        decimal actual)
    {
        Id = id;
        CostCentreCode = costCentreCode;
        FiscalYear = fiscalYear;
        Allotted = allotted;
        Reserved = reserved;
        Committed = committed;
        Actual = actual;
    }

    public Guid Id { get; private set; }

    public string CostCentreCode { get; private set; }

    /// <summary>The calendar year, in UTC.</summary>
    public int FiscalYear { get; private set; }

    public decimal Allotted { get; private set; }

    /// <summary>Set aside for submitted requisitions not yet ordered.</summary>
    public decimal Reserved { get; private set; }

    /// <summary>Purchase orders issued and not yet invoiced.</summary>
    public decimal Committed { get; private set; }

    /// <summary>Invoices matched.</summary>
    public decimal Actual { get; private set; }

    public decimal Consumed => Reserved + Committed + Actual;

    public decimal Available => Allotted - Consumed;

    /// <summary>
    /// How far consumption runs past the allotment. Only an invoice larger than its commitment can cause it,
    /// and it is reported, never hidden or refused.
    /// </summary>
    public decimal Overspend => Available < 0m ? -Available : 0m;

    public static (Budget Budget, LedgerEntry Opening) Open(
        Guid id,
        CostCentre costCentre,
        int fiscalYear,
        decimal allotted,
        Guid actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(costCentre);
        if (!costCentre.IsActive)
        {
            throw new BusinessRuleException(
                "cost_centre.inactive",
                $"Cost centre {costCentre.Code} is inactive and cannot be given a budget.");
        }

        if (fiscalYear is < EarliestFiscalYear or > LatestFiscalYear)
        {
            throw new BusinessRuleException(
                "budget.fiscal_year_invalid",
                $"A fiscal year is a calendar year from {EarliestFiscalYear} to {LatestFiscalYear}.",
                ViolationKind.Invalid);
        }

        Movement opening = Movement.Allot(Money.NotNegative(allotted, "The allotment"));
        var budget = new Budget(id, costCentre.Code, fiscalYear, opening.Allotted, 0m, 0m, 0m);
        return (budget, LedgerEntry.Record(id, id, LedgerStep.Open, opening, now, now, actorId));
    }

    /// <summary>True when the movement leaves available at zero or above.</summary>
    public bool CanAfford(Movement movement) => Available >= movement.Consumes;

    /// <summary>
    /// Checks a change to the allotment against the figures as read and returns the movement to apply. Raising
    /// is always allowed; lowering stops at what is already consumed.
    /// </summary>
    public Movement ChangeAllotment(decimal change)
    {
        Movement movement = Movement.Allot(change);
        if (change == 0m)
        {
            throw new BusinessRuleException(
                "budget.allotment_change_zero",
                "An allotment change has to raise or lower the allotment.",
                ViolationKind.Invalid);
        }

        if (change < 0m && !CanAfford(movement))
        {
            throw new BusinessRuleException(
                "budget.allotment_below_consumed",
                $"The allotment cannot go below the {Consumed:0.00} already reserved, committed or spent; it can be lowered by at most {Math.Max(Available, 0m):0.00}.");
        }

        return movement;
    }

    public void Apply(Movement movement)
    {
        decimal allotted = Allotted + movement.Allotted;
        decimal reserved = Reserved + movement.Reserved;
        decimal committed = Committed + movement.Committed;
        decimal actual = Actual + movement.Actual;
        if (reserved < 0m || committed < 0m || actual < 0m || reserved + committed > allotted)
        {
            throw new InvalidOperationException(
                $"Applying {movement} to budget {Id} would break its figures: allotted {allotted}, reserved {reserved}, committed {committed}, actual {actual}.");
        }

        (Allotted, Reserved, Committed, Actual) = (allotted, reserved, committed, actual);
    }
}
