using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class ApprovalTests
{
    [Fact]
    public void The_manager_alone_approves_a_small_requisition()
    {
        Requisition requisition = A.PendingApproval(10_000m);

        requisition.Approve(A.Manager, A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.Approved);
        requisition.CurrentStep.ShouldBeNull();
        ApprovalStep step = requisition.Steps.Single();
        step.Decision.ShouldBe(ApprovalDecision.Approved);
        step.DecidedBy.ShouldBe(A.Manager.Id);
        step.DecidedAt.ShouldBe(A.Now);
    }

    [Fact]
    public void Each_step_hands_over_to_the_next_until_the_last()
    {
        Requisition requisition = A.PendingApproval(250_000m);

        requisition.Approve(A.Manager, A.Now).ShouldBeFalse();
        requisition.CurrentStep.ShouldBe(2);
        requisition.Approve(A.Finance, A.Now).ShouldBeFalse();
        requisition.CurrentStep.ShouldBe(3);
        requisition.Approve(A.Cfo, A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.Approved);
        requisition.Steps.OrderBy(step => step.Sequence).Select(step => step.DecidedBy)
            .ShouldBe([A.Manager.Id, A.Finance.Id, A.Cfo.Id]);
    }

    [Fact]
    public void Nobody_approves_their_own_requisition_whatever_roles_they_hold()
    {
        Actor everything = A.Person(1, "rita", Roles.Requester, Roles.Approver, Roles.FinanceApprover, Roles.Cfo);
        Requisition requisition = A.PendingApproval(250_000m);
        requisition.Approve(A.Manager, A.Now);

        A.Refused(() => requisition.Approve(everything, A.Now), RequisitionCodes.SelfApproval, ViolationKind.Forbidden);
    }

    [Fact]
    public void Only_the_manager_on_record_takes_the_manager_step()
    {
        Requisition requisition = A.PendingApproval();

        A.Refused(() => requisition.Approve(A.OtherManager, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
        A.Refused(() => requisition.Approve(A.Finance, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
        A.Refused(() => requisition.Approve(A.Cfo, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
    }

    [Fact]
    public void The_finance_step_needs_the_finance_role()
    {
        Requisition requisition = A.PendingApproval(50_000m);
        requisition.Approve(A.Manager, A.Now);

        A.Refused(() => requisition.Approve(A.OtherManager, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
        A.Refused(() => requisition.Approve(A.Cfo, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
        requisition.Approve(A.OtherFinance, A.Now).ShouldBeTrue();
    }

    [Fact]
    public void The_cfo_step_needs_the_cfo_role()
    {
        Requisition requisition = A.PendingApproval(250_000m);
        requisition.Approve(A.Manager, A.Now);
        requisition.Approve(A.Finance, A.Now);

        A.Refused(() => requisition.Approve(A.OtherFinance, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
    }

    [Fact]
    public void Nobody_approves_two_steps_of_one_requisition()
    {
        Actor managerInFinance = A.Person(2, "mark", Roles.Approver, Roles.FinanceApprover);
        Requisition requisition = A.PendingApproval(50_000m);
        requisition.Approve(managerInFinance, A.Now);

        A.Refused(() => requisition.Approve(managerInFinance, A.Now), RequisitionCodes.DuplicateApprover, ViolationKind.Forbidden);
    }

    [Fact]
    public void Steps_are_taken_in_order()
    {
        Requisition requisition = A.PendingApproval(250_000m);

        A.Refused(() => requisition.Approve(A.Cfo, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);
        requisition.Steps.ShouldAllBe(step => step.Decision == ApprovalDecision.Pending);
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Submitted)]
    [InlineData(RequisitionStatus.Approved)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.Cancelled)]
    public void Only_a_requisition_pending_approval_can_be_approved(RequisitionStatus status) =>
        A.Refused(() => A.In(status).Approve(A.Manager, A.Now), RequisitionCodes.NotPendingApproval, ViolationKind.Conflict);

    [Fact]
    public void A_refused_approval_changes_nothing()
    {
        Requisition requisition = A.PendingApproval(50_000m);
        int revision = requisition.Revision;

        A.Refused(() => requisition.Approve(A.Finance, A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);

        requisition.CurrentStep.ShouldBe(1);
        requisition.Revision.ShouldBe(revision);
    }
}
