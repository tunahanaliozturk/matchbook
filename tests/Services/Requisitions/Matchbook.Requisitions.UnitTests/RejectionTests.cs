using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class RejectionTests
{
    [Fact]
    public void Whoever_may_take_the_current_step_may_reject_with_a_reason()
    {
        Requisition requisition = A.PendingApproval(50_000m);
        requisition.Approve(A.Manager, A.Now);

        requisition.Reject(A.Finance, "  Buy refurbished instead  ", A.Now);

        requisition.Status.ShouldBe(RequisitionStatus.Rejected);
        requisition.RejectionReason.ShouldBe("Buy refurbished instead");
        requisition.CurrentStep.ShouldBeNull();
        ApprovalStep finance = requisition.Steps.Single(step => step.Sequence == 2);
        finance.Decision.ShouldBe(ApprovalDecision.Rejected);
        finance.DecidedBy.ShouldBe(A.Finance.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_needs_a_reason(string reason) =>
        A.Refused(() => A.PendingApproval().Reject(A.Manager, reason, A.Now), RequisitionCodes.ReasonRequired, ViolationKind.Invalid);

    [Fact]
    public void A_reason_longer_than_the_limit_is_refused() =>
        A.Refused(
            () => A.PendingApproval().Reject(A.Manager, new string('x', Requisition.MaxReasonLength + 1), A.Now),
            RequisitionCodes.ReasonRequired,
            ViolationKind.Invalid);

    [Fact]
    public void The_requester_cannot_reject_their_own_requisition() =>
        A.Refused(() => A.PendingApproval().Reject(A.Requester, "Changed my mind", A.Now), RequisitionCodes.SelfApproval, ViolationKind.Forbidden);

    [Fact]
    public void Someone_the_step_does_not_wait_for_cannot_reject() =>
        A.Refused(() => A.PendingApproval().Reject(A.Cfo, "Too expensive", A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);

    [Fact]
    public void Who_is_asked_is_checked_before_what_they_wrote() =>
        A.Refused(() => A.PendingApproval().Reject(A.Cfo, "", A.Now), RequisitionCodes.NotYourStep, ViolationKind.Forbidden);

    [Fact]
    public void A_rejected_requisition_takes_no_further_decision() =>
        A.Refused(() => A.In(RequisitionStatus.Rejected).Approve(A.Manager, A.Now), RequisitionCodes.NotPendingApproval, ViolationKind.Conflict);
}
