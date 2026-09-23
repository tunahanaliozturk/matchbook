using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.Suppliers;

namespace Matchbook.Payables.UnitTests;

/// <summary>The local copies are built from events that may arrive twice and in any order.</summary>
public sealed class LocalCopyTests
{
    [Fact]
    public void An_order_first_mentioned_by_something_else_is_not_issued_until_its_own_event_arrives()
    {
        PurchaseOrder order = PurchaseOrder.FirstMentioned(A.OrderId);

        order.IsIssued.ShouldBeFalse();
        order.RecordIssue("PO-2026-000001", A.SupplierId, [A.Ordered(1, 5, 2m)], A.Now).ShouldBeTrue();
        order.IsIssued.ShouldBeTrue();
        order.Line(1).ShouldBe(A.Ordered(1, 5, 2m));
    }

    [Fact]
    public void A_redelivered_issue_changes_nothing()
    {
        PurchaseOrder order = A.IssuedOrder(A.Ordered(1, 5, 2m));

        order.RecordIssue("PO-OTHER", A.OtherSupplierId, [A.Ordered(1, 9, 9m), A.Ordered(2, 1, 1m)], A.Now).ShouldBeFalse();

        order.SupplierId.ShouldBe(A.SupplierId);
        order.Lines.ShouldBe([A.Ordered(1, 5, 2m)]);
    }

    [Fact]
    public void A_closure_is_kept_even_before_the_order_arrives_and_a_second_one_changes_nothing()
    {
        PurchaseOrder order = PurchaseOrder.FirstMentioned(A.OrderId);

        order.RecordClosure("Cancelled", cancelled: true, A.Now).ShouldBeTrue();
        order.RecordClosure("Completed", cancelled: false, A.Now).ShouldBeFalse();

        order.IsCancelled.ShouldBeTrue();
        order.CloseReason.ShouldBe("Cancelled");
    }

    [Fact]
    public void Every_revision_moves_the_orders_concurrency_token()
    {
        PurchaseOrder order = PurchaseOrder.FirstMentioned(A.OrderId);

        order.Revise();
        order.Revise();

        order.Revision.ShouldBe(2);
    }

    [Fact]
    public void A_receipt_that_lists_a_line_twice_counts_both()
    {
        Receipt receipt = Receipt.Record(Guid.NewGuid(), A.OrderId, [new ReceivedLine(2, 1.5m), new ReceivedLine(1, 3m), new ReceivedLine(2, 0.5m)], A.Now);

        receipt.Lines.ShouldBe([new ReceivedLine(1, 3m), new ReceivedLine(2, 2m)]);
    }

    [Fact]
    public void A_newer_supplier_snapshot_replaces_the_local_copy()
    {
        Supplier supplier = A.Supplier(A.SupplierId);

        supplier.Apply(Snapshot(version: 2, active: false, accountVersion: 3)).ShouldBeTrue();

        supplier.Version.ShouldBe(2);
        supplier.IsActive.ShouldBeFalse();
        supplier.Account!.AccountVersion.ShouldBe(3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void A_snapshot_that_is_not_newer_changes_nothing(long version)
    {
        Supplier supplier = A.Supplier(A.SupplierId);

        supplier.Apply(Snapshot(version, active: false, accountVersion: 9)).ShouldBeFalse();

        supplier.IsActive.ShouldBeTrue();
        supplier.Account!.AccountVersion.ShouldBe(1);
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, null, false)]
    public void A_supplier_can_be_paid_only_when_active_with_a_verified_account(bool active, int? accountVersion, bool payable) =>
        A.Supplier(A.SupplierId, active, accountVersion).CanBePaid.ShouldBe(payable);

    private static SupplierSnapshot Snapshot(long version, bool active, int accountVersion) =>
        new(version, "ACME", active, 45, A.Account(accountVersion), A.Now);
}
