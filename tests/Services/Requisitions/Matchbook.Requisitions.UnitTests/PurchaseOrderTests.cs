using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.UnitTests;

public sealed class PurchaseOrderTests
{
    private static readonly Guid OrderId = Guid.Parse("c0000000-0000-4000-8000-000000000001");

    [Fact]
    public void An_issued_order_moves_an_approved_requisition_to_ordered()
    {
        Requisition requisition = A.Approved();

        requisition.RecordPurchaseOrderIssued(OrderId, "PO-2026-000017", A.Buyer.Id, A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.Ordered);
        requisition.PurchaseOrderId.ShouldBe(OrderId);
        requisition.PurchaseOrderNumber.ShouldBe("PO-2026-000017");
    }

    [Fact]
    public void A_redelivered_issue_changes_nothing()
    {
        Requisition requisition = A.Approved();
        requisition.RecordPurchaseOrderIssued(OrderId, "PO-2026-000017", A.Buyer.Id, A.Now);
        int revision = requisition.Revision;

        requisition.RecordPurchaseOrderIssued(OrderId, "PO-2026-000017", A.Buyer.Id, A.Now).ShouldBeFalse();

        requisition.Revision.ShouldBe(revision);
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Submitted)]
    [InlineData(RequisitionStatus.PendingApproval)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.Cancelled)]
    public void An_issue_replayed_before_approval_cannot_skip_it(RequisitionStatus status)
    {
        Requisition requisition = A.In(status);

        requisition.RecordPurchaseOrderIssued(OrderId, "PO-2026-000017", A.Buyer.Id, A.Now).ShouldBeFalse();

        requisition.Status.ShouldBe(status);
        requisition.PurchaseOrderNumber.ShouldBeNull();
    }

    [Theory]
    [InlineData(RequisitionStatus.Approved)]
    [InlineData(RequisitionStatus.Ordered)]
    public void A_closed_order_closes_the_requisition_whether_or_not_it_was_issued(RequisitionStatus status)
    {
        Requisition requisition = A.In(status);

        requisition.RecordPurchaseOrderClosed(OrderId, "Cancelled", A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.Closed);
        requisition.PurchaseOrderId.ShouldNotBeNull();
    }

    [Fact]
    public void An_issue_that_arrives_after_the_close_keeps_the_order_number_and_the_status()
    {
        Requisition requisition = A.Approved();
        requisition.RecordPurchaseOrderClosed(OrderId, "Completed", A.Now);

        requisition.RecordPurchaseOrderIssued(OrderId, "PO-2026-000017", A.Buyer.Id, A.Now).ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.Closed);
        requisition.PurchaseOrderNumber.ShouldBe("PO-2026-000017");
    }

    [Theory]
    [InlineData(RequisitionStatus.PendingApproval)]
    [InlineData(RequisitionStatus.Closed)]
    [InlineData(RequisitionStatus.Cancelled)]
    public void A_close_for_a_requisition_that_is_not_approved_or_ordered_changes_nothing(RequisitionStatus status)
    {
        Requisition requisition = A.In(status);
        int revision = requisition.Revision;

        requisition.RecordPurchaseOrderClosed(OrderId, "ShortClosed", A.Now).ShouldBeFalse();

        requisition.Status.ShouldBe(status);
        requisition.Revision.ShouldBe(revision);
    }
}
