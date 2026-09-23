using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class ReservationTests
{
    [Theory]
    [InlineData("0", new[] { ApprovalStepKind.Manager })]
    [InlineData("10000.00", new[] { ApprovalStepKind.Manager })]
    [InlineData("10000.01", new[] { ApprovalStepKind.Manager, ApprovalStepKind.Finance })]
    [InlineData("100000.00", new[] { ApprovalStepKind.Manager, ApprovalStepKind.Finance })]
    [InlineData("100000.01", new[] { ApprovalStepKind.Manager, ApprovalStepKind.Finance, ApprovalStepKind.Cfo })]
    public void Reserved_funds_fix_a_route_whose_length_follows_the_amount(string amount, ApprovalStepKind[] route)
    {
        Requisition requisition = A.Submitted(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        requisition.RecordFundsReserved(A.Manager.Id, A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.PendingApproval);
        requisition.CurrentStep.ShouldBe(1);
        requisition.Steps.OrderBy(step => step.Sequence).Select(step => step.Kind).ShouldBe(route);
        requisition.Steps.ShouldAllBe(step => step.Decision == ApprovalDecision.Pending);
    }

    [Fact]
    public void The_manager_step_is_bound_to_the_manager_on_record_when_funds_arrive()
    {
        Requisition requisition = A.Submitted(50_000m);

        requisition.RecordFundsReserved(A.OtherManager.Id, A.Now);

        ApprovalStep[] steps = [.. requisition.Steps.OrderBy(step => step.Sequence)];
        steps[0].ApproverId.ShouldBe(A.OtherManager.Id);
        steps[0].RequiredRole.ShouldBeNull();
        steps[1].ApproverId.ShouldBeNull();
        steps[1].RequiredRole.ShouldBe(Roles.FinanceApprover);
    }

    [Fact]
    public void A_redelivered_reservation_builds_no_second_route()
    {
        Requisition requisition = A.PendingApproval(50_000m);
        int revision = requisition.Revision;

        requisition.RecordFundsReserved(A.OtherManager.Id, A.Now).ShouldBeFalse();

        requisition.Steps.Count.ShouldBe(2);
        requisition.Steps.Single(step => step.Sequence == 1).ApproverId.ShouldBe(A.Manager.Id);
        requisition.Revision.ShouldBe(revision);
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Cancelled)]
    [InlineData(RequisitionStatus.BudgetRejected)]
    [InlineData(RequisitionStatus.Approved)]
    public void A_reservation_for_a_requisition_not_waiting_for_one_changes_nothing(RequisitionStatus status)
    {
        Requisition requisition = A.In(status);
        int steps = requisition.Steps.Count;

        requisition.RecordFundsReserved(A.Manager.Id, A.Now).ShouldBeFalse();

        requisition.Status.ShouldBe(status);
        requisition.Steps.Count.ShouldBe(steps);
    }

    [Fact]
    public void A_refused_reservation_ends_the_requisition_with_budgets_reason()
    {
        Requisition requisition = A.Submitted();

        requisition.RecordFundsRefused("insufficient_funds", A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.BudgetRejected);
        requisition.RejectionReason.ShouldBe("insufficient_funds");
        requisition.Steps.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(RequisitionStatus.Cancelled)]
    [InlineData(RequisitionStatus.PendingApproval)]
    [InlineData(RequisitionStatus.BudgetRejected)]
    public void A_refusal_for_a_requisition_not_waiting_for_one_changes_nothing(RequisitionStatus status)
    {
        Requisition requisition = A.In(status);
        string? reason = requisition.RejectionReason;

        requisition.RecordFundsRefused("no_budget", A.Now).ShouldBeFalse();

        requisition.Status.ShouldBe(status);
        requisition.RejectionReason.ShouldBe(reason);
    }
}
