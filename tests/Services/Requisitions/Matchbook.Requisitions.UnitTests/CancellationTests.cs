using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class CancellationTests
{
    [Theory]
    [InlineData(RequisitionStatus.Draft, false)]
    [InlineData(RequisitionStatus.Submitted, true)]
    [InlineData(RequisitionStatus.PendingApproval, true)]
    public void The_requester_cancels_before_approval_and_learns_whether_budgets_must_be_told(
        RequisitionStatus status,
        bool mightHoldReservation)
    {
        Requisition requisition = A.In(status);

        requisition.Cancel(A.Requester, A.Now).ShouldBe(mightHoldReservation);

        requisition.Status.ShouldBe(RequisitionStatus.Cancelled);
        requisition.CurrentStep.ShouldBeNull();
    }

    [Fact]
    public void Nobody_else_cancels_a_requisition() =>
        A.Refused(() => A.PendingApproval().Cancel(A.Manager, A.Now), RequisitionCodes.NotRequester, ViolationKind.Forbidden);

    [Theory]
    [InlineData(RequisitionStatus.Approved)]
    [InlineData(RequisitionStatus.Ordered)]
    [InlineData(RequisitionStatus.Closed)]
    [InlineData(RequisitionStatus.BudgetRejected)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.Cancelled)]
    public void Once_approved_or_ended_a_requisition_cannot_be_cancelled(RequisitionStatus status) =>
        A.Refused(() => A.In(status).Cancel(A.Requester, A.Now), RequisitionCodes.NotCancellable, ViolationKind.Conflict);

    [Fact]
    public void A_cancelled_requisition_leaves_nothing_for_approvers()
    {
        Requisition requisition = A.PendingApproval();
        requisition.Cancel(A.Requester, A.Now);

        A.Refused(() => requisition.Approve(A.Manager, A.Now), RequisitionCodes.NotPendingApproval, ViolationKind.Conflict);
    }
}
