using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

public sealed class ClosingTests
{
    [Fact]
    public void A_buyer_can_short_close_an_issued_order_with_goods_still_to_come()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 10m), (2, 1m));

        order.ShortClose(People.Bruno, Orders.Now);

        order.Status.ShouldBe(PurchaseOrderStatus.ShortClosed);
        order.ClosedBy.ShouldBe(People.Bruno.Id);
        order.ClosedAt.ShouldBe(Orders.Now);
    }

    [Fact]
    public void An_order_received_in_full_but_invoiced_in_part_can_be_short_closed_so_a_missing_invoice_cannot_hold_it_open()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 10m), (2, 4m));
        order.Invoice((1, 10m), (2, 3m));

        order.ShortClose(People.Bruno, Orders.Now);

        order.Status.ShouldBe(PurchaseOrderStatus.ShortClosed);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("short-closed")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    public void Only_an_issued_order_can_be_short_closed(string state) =>
        ShouldBeRefused(
            () => Orders.InState(state).ShortClose(People.Bruno, Orders.Now), "purchase_order.not_issued", ViolationKind.Conflict);

    [Theory]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("issued")]
    public void A_buyer_can_cancel_an_order_before_anything_is_received(string state)
    {
        PurchaseOrder order = Orders.InState(state);

        order.Cancel(People.Beth, Orders.Now);

        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        order.ClosedBy.ShouldBe(People.Beth.Id);
        order.ClosedAt.ShouldBe(Orders.Now);
    }

    [Fact]
    public void An_order_with_goods_received_cannot_be_cancelled()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((2, 0.5m));

        ShouldBeRefused(() => order.Cancel(People.Bruno, Orders.Now), "purchase_order.goods_received", ViolationKind.Conflict);
        order.Status.ShouldBe(PurchaseOrderStatus.Issued);
    }

    [Theory]
    [InlineData("short-closed")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    public void A_closed_order_cannot_be_cancelled(string state) =>
        ShouldBeRefused(() => Orders.InState(state).Cancel(People.Bruno, Orders.Now), "purchase_order.closed", ViolationKind.Conflict);

    [Fact]
    public void Only_a_buyer_may_close_an_order()
    {
        ShouldBeRefused(
            () => Orders.Issued().ShortClose(People.Rosa, Orders.Now), "purchase_order.not_a_buyer", ViolationKind.Forbidden);
        ShouldBeRefused(
            () => Orders.Draft().Cancel(People.Audrey, Orders.Now), "purchase_order.not_a_buyer", ViolationKind.Forbidden);
    }
}
