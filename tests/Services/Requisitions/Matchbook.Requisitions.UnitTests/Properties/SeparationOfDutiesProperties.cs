using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests.Properties;

/// <summary>
/// A requisition of any amount, a handful of people holding any mix of approval roles (the requester
/// included), a manager picked from among them (the requester too, as a race between submitting and a
/// change of manager could leave it), and any sequence of approvals and rejections by any of them.
/// </summary>
public sealed class SeparationOfDutiesProperties
{
    private static readonly string[] ApprovalRoles = [Roles.Approver, Roles.FinanceApprover, Roles.Cfo];

    private static readonly Gen<decimal> Amounts = Gen.OneOf(
        Gen.Choose(0, 1_000_000).Select(static cents => cents / 100m),
        Gen.Choose(1_000_001, 10_000_000).Select(static cents => cents / 100m),
        Gen.Choose(10_000_001, 50_000_000).Select(static cents => cents / 100m));

    private static readonly Gen<Scenario> Scenarios =
        from amount in Amounts
        from size in Gen.Choose(2, 6)
        from roles in Gen.ListOf(Gen.SubListOf(ApprovalRoles), size)
        from managerIndex in Gen.Choose(0, size - 1)
        from attempts in Gen.ListOf(Attempt(size))
        select new Scenario(
            amount,
            [.. roles.Select(static (held, index) => A.Person(100 + index, $"person{index}", [.. held]))],
            managerIndex,
            [.. attempts]);

    [Property(MaxTest = 300)]
    public Property A_decision_is_accepted_exactly_when_the_rules_allow_it() =>
        Prop.ForAll(Scenarios.ToArbitrary(), static scenario =>
        {
            Requisition requisition = scenario.PendingApproval();

            foreach ((int who, bool approve) in scenario.Attempts)
            {
                Actor actor = scenario.People[who];
                bool allowed = MayDecide(requisition, scenario, actor);

                bool accepted = Try(() =>
                {
                    if (approve)
                    {
                        requisition.Approve(actor, A.Now);
                    }
                    else
                    {
                        requisition.Reject(actor, "Not now", A.Now);
                    }
                });

                accepted.ShouldBe(allowed, $"{actor.Name} holding [{string.Join(", ", actor.Roles)}] at step {requisition.CurrentStep}");
            }
        });

    [Property(MaxTest = 300)]
    public Property Nobody_decides_their_own_requisition_or_two_steps_of_it_and_steps_go_in_order() =>
        Prop.ForAll(Scenarios.ToArbitrary(), static scenario =>
        {
            Requisition requisition = scenario.PendingApproval();
            foreach ((int who, bool approve) in scenario.Attempts)
            {
                Actor actor = scenario.People[who];
                Try(() =>
                {
                    if (approve)
                    {
                        requisition.Approve(actor, A.Now);
                    }
                    else
                    {
                        requisition.Reject(actor, "Not now", A.Now);
                    }
                });
            }

            ApprovalStep[] steps = [.. requisition.Steps.OrderBy(static step => step.Sequence)];
            Guid[] deciders = [.. steps.Where(static step => step.DecidedBy.HasValue).Select(static step => step.DecidedBy!.Value)];

            deciders.ShouldNotContain(scenario.Requester.Id);
            deciders.ShouldBeUnique();
            steps.Where(static step => step.Kind == ApprovalStepKind.Manager && step.DecidedBy.HasValue)
                .ShouldAllBe(step => step.DecidedBy == scenario.ManagerId);
            steps.Where(static step => step.DecidedBy.HasValue && step.RequiredRole != null)
                .ShouldAllBe(step => scenario.Holder(step.DecidedBy!.Value).IsIn(step.RequiredRole!));

            // Decided steps form a prefix of the route, every one but the last of them approved.
            int decided = steps.Count(static step => step.Decision != ApprovalDecision.Pending);
            steps.Take(decided).ShouldAllBe(step => step.Decision != ApprovalDecision.Pending);
            steps.Take(Math.Max(0, decided - 1)).ShouldAllBe(step => step.Decision == ApprovalDecision.Approved);
            (requisition.Status == RequisitionStatus.Approved)
                .ShouldBe(steps.All(static step => step.Decision == ApprovalDecision.Approved));
        });

    // The rule restated independently of the domain's code: pending, not the requester's own, nothing of
    // it decided by this person yet, and the current step one this person may take.
    private static bool MayDecide(Requisition requisition, Scenario scenario, Actor actor)
    {
        if (requisition.Status != RequisitionStatus.PendingApproval
            || actor.Id == scenario.Requester.Id
            || requisition.Steps.Any(step => step.DecidedBy == actor.Id))
        {
            return false;
        }

        return requisition.Steps.Single(step => step.Sequence == requisition.CurrentStep).Kind switch
        {
            ApprovalStepKind.Manager => actor.Id == scenario.ManagerId,
            ApprovalStepKind.Finance => actor.Roles.Contains(Roles.FinanceApprover),
            ApprovalStepKind.Cfo => actor.Roles.Contains(Roles.Cfo),
            _ => false,
        };
    }

    private static Gen<(int Who, bool Approve)> Attempt(int size) =>
        from who in Gen.Choose(0, size - 1)
        from approve in Gen.Frequency((5, Gen.Constant(true)), (1, Gen.Constant(false)))
        select (who, approve);

    private static bool Try(Action decide)
    {
        try
        {
            decide();
            return true;
        }
        catch (BusinessRuleException)
        {
            return false;
        }
    }

    public sealed record Scenario(decimal Amount, IReadOnlyList<Actor> People, int ManagerIndex, IReadOnlyList<(int Who, bool Approve)> Attempts)
    {
        public Actor Requester => People[0];

        public Guid ManagerId => People[ManagerIndex].Id;

        public Actor Holder(Guid id) => People.Single(person => person.Id == id);

        /// <summary>
        /// Submitted against a cost centre someone outside the group manages, so submitting passes, then routed
        /// to the manager picked from the group, as if the manager changed in between.
        /// </summary>
        public Requisition PendingApproval()
        {
            Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, Requester, A.DetailsWorth(Amount), A.Now);
            requisition.Submit(Requester, A.CostCentre(), A.Supplier(), A.Now);
            requisition.RecordFundsReserved(ManagerId, A.Now);
            return requisition;
        }

        public override string ToString() =>
            $"amount {Amount}, manager person{ManagerIndex}, people [{string.Join("; ", People.Select(person => $"{person.Name}: {string.Join(",", person.Roles)}"))}], attempts [{string.Join(" ", Attempts.Select(attempt => $"{(attempt.Approve ? "+" : "-")}{attempt.Who}"))}]";
    }
}
