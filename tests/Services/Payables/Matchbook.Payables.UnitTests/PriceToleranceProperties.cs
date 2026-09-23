using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

/// <summary>
/// The tolerance at its boundary, in both regimes: where 2% of the ordered line amount is the smaller limit, and
/// where the 100.00 cap is. The boundary is worked out here from the rule as the design states it, not from the code
/// under test: the largest price step (a unit price has four decimals) whose variance fits, and the step after it.
/// </summary>
public sealed class PriceToleranceProperties
{
    private const decimal PriceStep = 0.0001m;

    [Property(MaxTest = 300)]
    public Property A_price_at_the_edge_of_tolerance_matches_and_one_step_beyond_does_not() =>
        Prop.ForAll(Lines.ToArbitrary(), line =>
        {
            decimal edge = LargestStepWithin(line);

            OutcomeAt(line, line.OrderedPrice + edge).ShouldBe(MatchOutcome.Matched);
            OutcomeAt(line, line.OrderedPrice + edge + PriceStep).ShouldBe(MatchOutcome.PriceVariance);
        });

    [Property(MaxTest = 300)]
    public Property The_same_edge_holds_below_the_ordered_price() =>
        Prop.ForAll(Lines.ToArbitrary(), line =>
        {
            decimal edge = LargestStepWithin(line);
            decimal atEdge = line.OrderedPrice - edge;
            decimal beyond = atEdge - PriceStep;

            // An invoice line has to be worth a cent, so the tiniest lines have no price below the order to test.
            if (Amounts.Line(line.Quantity, atEdge) > 0)
            {
                OutcomeAt(line, atEdge).ShouldBe(MatchOutcome.Matched);
            }

            if (beyond >= 0 && Amounts.Line(line.Quantity, beyond) > 0)
            {
                OutcomeAt(line, beyond).ShouldBe(MatchOutcome.PriceVariance);
            }
        });

    [Fact]
    public void The_generated_lines_cover_both_regimes()
    {
        TolerancedLine[] sample = Lines.Sample(100, 500);

        sample.ShouldContain(line => 0.02m * Amounts.Line(line.Quantity, line.OrderedPrice) < 100m);
        sample.ShouldContain(line => 0.02m * Amounts.Line(line.Quantity, line.OrderedPrice) > 100m);
    }

    private sealed record TolerancedLine(decimal Quantity, decimal OrderedPrice);

    // Small lines, where the percentage binds, and lines worth 5,000.00 or more, where the cap does. The large ones
    // include single units at high prices, where one price step moves the variance by a hundredth of a cent, so the
    // cap is pinned to the cent and not just roughly. Every line is worth at least a cent.
    private static readonly Gen<TolerancedLine> Lines =
        Gen.OneOf(
                from quantity in Gen.Choose(1, 100_000)
                from price in Gen.Choose(100, 400_000)
                select new TolerancedLine(quantity / 1000m, price / 10_000m),
                from quantity in Gen.Choose(1_000, 5_000_000)
                from price in Gen.Choose(50_000_000, 100_000_000)
                select new TolerancedLine(quantity / 1000m, price / 10_000m))
            .Where(line => Amounts.Line(line.Quantity, line.OrderedPrice) >= 0.01m);

    private static decimal LargestStepWithin(TolerancedLine line)
    {
        decimal allowed = Math.Min(0.02m * Amounts.Line(line.Quantity, line.OrderedPrice), 100.00m);
        decimal edge = Math.Floor(allowed / line.Quantity / PriceStep) * PriceStep;

        // The oracle itself, checked: the edge fits and the next step does not.
        (edge * line.Quantity).ShouldBeLessThanOrEqualTo(allowed);
        ((edge + PriceStep) * line.Quantity).ShouldBeGreaterThan(allowed);
        return edge;
    }

    private static MatchOutcome OutcomeAt(TolerancedLine line, decimal invoicedPrice) =>
        ThreeWayMatch.Evaluate(
                A.Invoice(A.Line(1, line.Quantity, invoicedPrice)),
                A.IssuedOrder(A.Ordered(1, line.Quantity, line.OrderedPrice)),
                A.Received((1, line.Quantity)))
            .Outcome;
}
