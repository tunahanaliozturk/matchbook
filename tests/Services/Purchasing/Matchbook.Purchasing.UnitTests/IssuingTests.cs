using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

public sealed class IssuingTests
{
    private static readonly DateTimeOffset Later = Orders.Now.AddMinutes(5);

    [Fact]
    public void Issuing_sends_the_order_for_commitment_as_attempt_one()
    {
        PurchaseOrder order = Orders.Draft();

        order.RequestIssue(People.Bruno, Orders.ActiveSupplierOf(order));

        order.Status.ShouldBe(PurchaseOrderStatus.CommitmentPending);
        order.CommitmentAttempt.ShouldBe(1);
        order.IssuedBy.ShouldBe(People.Bruno.Id);
        order.IssuedAt.ShouldBeNull();
    }

    [Fact]
    public void A_supplier_purchasing_has_never_heard_of_is_not_active()
    {
        PurchaseOrder order = Orders.Draft();

        ShouldBeRefused(() => order.RequestIssue(People.Bruno, null), "purchase_order.supplier_not_active", ViolationKind.Conflict);
        order.Status.ShouldBe(PurchaseOrderStatus.Draft);
        order.CommitmentAttempt.ShouldBe(0);
    }

    [Fact]
    public void An_inactive_supplier_cannot_be_ordered_from()
    {
        PurchaseOrder order = Orders.Draft();

        ShouldBeRefused(
            () => order.RequestIssue(People.Bruno, new Supplier(order.SupplierId, 3, isActive: false)),
            "purchase_order.supplier_not_active",
            ViolationKind.Conflict);
    }

    [Fact]
    public void The_supplier_passed_in_must_be_the_orders_own()
    {
        PurchaseOrder order = Orders.Draft();

        Should.Throw<ArgumentException>(() => order.RequestIssue(People.Bruno, new Supplier(Guid.CreateVersion7(), 1, true)));
    }

    [Fact]
    public void An_order_with_every_line_at_zero_cannot_be_issued()
    {
        PurchaseOrder order = Orders.Draft((0m, 5m), (0m, 7m));

        ShouldBeRefused(
            () => order.RequestIssue(People.Bruno, Orders.ActiveSupplierOf(order)),
            "purchase_order.nothing_ordered",
            ViolationKind.Conflict);
    }

    [Fact]
    public void Only_a_buyer_may_issue()
    {
        PurchaseOrder order = Orders.Draft();

        ShouldBeRefused(
            () => order.RequestIssue(People.Audrey, Orders.ActiveSupplierOf(order)),
            "purchase_order.not_a_buyer",
            ViolationKind.Forbidden);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("issued")]
    [InlineData("cancelled")]
    public void Only_a_draft_can_be_issued(string state)
    {
        PurchaseOrder order = Orders.InState(state);

        ShouldBeRefused(
            () => order.RequestIssue(People.Bruno, Orders.ActiveSupplierOf(order)),
            "purchase_order.not_draft",
            ViolationKind.Conflict);
    }

    [Fact]
    public void Committed_funds_for_the_attempt_in_flight_issue_the_order()
    {
        PurchaseOrder order = Orders.Pending();

        order.ConfirmCommitment(1, Later).ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Issued);
        order.IssuedAt.ShouldBe(Later);
        order.IssuedBy.ShouldBe(People.Bruno.Id);
    }

    [Fact]
    public void A_rejection_for_the_attempt_in_flight_returns_the_order_to_draft_with_the_reason()
    {
        PurchaseOrder order = Orders.Pending();

        order.RejectCommitment(1, "insufficient_funds").ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Draft);
        order.CommitmentRejectionReason.ShouldBe("insufficient_funds");
        order.CommitmentAttempt.ShouldBe(1);
        order.IssuedBy.ShouldBeNull();
    }

    [Fact]
    public void Issuing_again_after_a_rejection_is_the_next_attempt_and_clears_the_reason()
    {
        PurchaseOrder order = Orders.Pending();
        order.RejectCommitment(1, "insufficient_funds");
        order.AmendLine(People.Beth, 1, 5m, 12.50m);

        order.RequestIssue(People.Beth, Orders.ActiveSupplierOf(order));

        order.CommitmentAttempt.ShouldBe(2);
        order.CommitmentRejectionReason.ShouldBeNull();
        order.IssuedBy.ShouldBe(People.Beth.Id);
    }

    [Fact]
    public void A_reply_to_an_earlier_attempt_is_ignored()
    {
        PurchaseOrder order = Orders.Pending();
        order.RejectCommitment(1, "insufficient_funds");
        order.RequestIssue(People.Bruno, Orders.ActiveSupplierOf(order));

        order.ConfirmCommitment(1, Later).ShouldBeFalse();
        order.RejectCommitment(1, "no_budget").ShouldBeFalse();

        order.Status.ShouldBe(PurchaseOrderStatus.CommitmentPending);
        order.CommitmentAttempt.ShouldBe(2);
        order.CommitmentRejectionReason.ShouldBeNull();
    }

    [Fact]
    public void A_reply_to_an_attempt_never_sent_is_ignored()
    {
        PurchaseOrder order = Orders.Pending();

        order.ConfirmCommitment(2, Later).ShouldBeFalse();

        order.Status.ShouldBe(PurchaseOrderStatus.CommitmentPending);
    }

    [Fact]
    public void A_redelivered_confirmation_changes_nothing()
    {
        PurchaseOrder order = Orders.Pending();
        order.ConfirmCommitment(1, Orders.Now);

        order.ConfirmCommitment(1, Later).ShouldBeFalse();

        order.IssuedAt.ShouldBe(Orders.Now);
    }

    [Fact]
    public void A_redelivered_rejection_changes_nothing()
    {
        PurchaseOrder order = Orders.Pending();
        order.RejectCommitment(1, "insufficient_funds");

        order.RejectCommitment(1, "no_budget").ShouldBeFalse();

        order.CommitmentRejectionReason.ShouldBe("insufficient_funds");
    }

    [Fact]
    public void A_late_confirmation_does_not_bring_a_cancelled_order_back()
    {
        PurchaseOrder order = Orders.Pending();
        order.Cancel(People.Bruno, Orders.Now);

        order.ConfirmCommitment(1, Later).ShouldBeFalse();

        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        order.IssuedAt.ShouldBeNull();
    }
}
