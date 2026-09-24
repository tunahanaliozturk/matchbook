using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.UnitTests.Properties;

public sealed class RoutingProperties
{
    private static readonly ApprovalStepKind[] FullRoute = [ApprovalStepKind.Manager, ApprovalStepKind.Finance, ApprovalStepKind.Cfo];

    // Uniform amounts would almost never land on a boundary, so two thirds of them are within three cents of
    // one, which puts roughly one in ten exactly on it.
    private static readonly Gen<decimal> Amounts = Gen.OneOf(
        Gen.Choose(0, int.MaxValue).Select(static cents => cents / 100m),
        Gen.Choose(-3, 3).Select(static cents => 10_000m + (cents / 100m)),
        Gen.Choose(-3, 3).Select(static cents => 100_000m + (cents / 100m)));

    [Property]
    public Property The_route_is_the_manager_plus_one_step_per_threshold_strictly_exceeded() =>
        Prop.ForAll(Amounts.ToArbitrary(), static amount =>
        {
            Requisition requisition = A.Submitted(amount);

            requisition.RecordFundsReserved(A.Manager.Id, A.Now);

            // Stated from the design document, not from ApprovalRoute, so the thresholds are pinned here.
            int expected = 1 + (amount > 10_000m ? 1 : 0) + (amount > 100_000m ? 1 : 0);
            ApprovalStep[] steps = [.. requisition.Steps.OrderBy(static step => step.Sequence)];
            steps.Select(static step => step.Kind).ShouldBe(FullRoute.Take(expected));
            steps.Select(static step => step.Sequence).ShouldBe(Enumerable.Range(1, expected));
            requisition.CurrentStep.ShouldBe(1);
        });
}
