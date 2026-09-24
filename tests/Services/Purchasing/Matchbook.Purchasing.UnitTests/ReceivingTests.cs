using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

public sealed class ReceivingTests
{
    private static readonly Guid ReceiptId = Guid.CreateVersion7();

    [Fact]
    public void A_receiver_records_what_arrived_and_the_receipt_says_who_when_and_how_much()
    {
        PurchaseOrder order = Orders.Issued();

        GoodsReceipt receipt = order.RecordReceipt(People.Rosa, ReceiptId, [new LineQuantity(1, 4m), new LineQuantity(2, 1.5m)], Orders.Now);

        receipt.Id.ShouldBe(ReceiptId);
        receipt.PurchaseOrderId.ShouldBe(order.Id);
        receipt.ReceivedBy.ShouldBe(People.Rosa.Id);
        receipt.ReceivedAt.ShouldBe(Orders.Now);
        receipt.Lines.Select(static line => (line.LineNumber, line.Quantity)).ShouldBe([(1, 4m), (2, 1.5m)]);
        order.Line(1).ReceivedQuantity.ShouldBe(4m);
        order.Line(2).ReceivedQuantity.ShouldBe(1.5m);
        order.Line(2).OpenQuantity.ShouldBe(2.5m);
    }

    [Fact]
    public void Receipts_add_up_and_a_line_can_be_received_in_full()
    {
        PurchaseOrder order = Orders.Issued();

        GoodsReceipt first = order.Receive((1, 6m));
        GoodsReceipt second = order.Receive((1, 4m));

        second.Id.ShouldNotBe(first.Id);
        second.Lines.Single().Quantity.ShouldBe(4m);
        order.Line(1).ReceivedQuantity.ShouldBe(10m);
        order.Line(1).OpenQuantity.ShouldBe(0m);
    }

    [Fact]
    public void Receiving_more_than_is_open_is_refused_and_no_line_of_the_receipt_is_taken()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((2, 3m));

        ShouldBeRefused(() => order.Receive((1, 2m), (2, 1.001m)), "purchase_order.over_receipt", ViolationKind.Conflict);

        order.Line(1).ReceivedQuantity.ShouldBe(0m);
        order.Line(2).ReceivedQuantity.ShouldBe(3m);
    }

    [Fact]
    public void The_buyer_who_issued_the_order_may_not_receive_against_it_even_holding_the_receiver_role()
    {
        PurchaseOrder order = Orders.Issued();

        ShouldBeRefused(
            () => order.RecordReceipt(People.BrunoAsReceiver, Guid.CreateVersion7(), [new LineQuantity(1, 1m)], Orders.Now),
            "purchase_order.receiver_is_buyer",
            ViolationKind.Forbidden);
        order.Line(1).ReceivedQuantity.ShouldBe(0m);
    }

    [Fact]
    public void A_buyer_who_did_not_issue_the_order_may_receive_it_holding_the_receiver_role()
    {
        PurchaseOrder order = Orders.Issued();

        order.RecordReceipt(People.BethAsReceiver, Guid.CreateVersion7(), [new LineQuantity(1, 1m)], Orders.Now);

        order.Line(1).ReceivedQuantity.ShouldBe(1m);
    }

    [Fact]
    public void Only_a_receiver_may_record_a_receipt() =>
        ShouldBeRefused(
            () => Orders.Issued().RecordReceipt(People.Beth, Guid.CreateVersion7(), [new LineQuantity(1, 1m)], Orders.Now),
            "purchase_order.not_a_receiver",
            ViolationKind.Forbidden);

    [Theory]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("short-closed")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    public void Goods_are_received_only_against_an_issued_order(string state) =>
        ShouldBeRefused(() => Orders.InState(state).Receive((1, 0.001m)), "purchase_order.not_issued", ViolationKind.Conflict);

    [Fact]
    public void A_receipt_needs_at_least_one_line() =>
        ShouldBeRefused(() => Orders.Issued().Receive(), "purchase_order.empty_receipt", ViolationKind.Invalid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Every_quantity_on_a_receipt_is_above_zero(decimal quantity) =>
        ShouldBeRefused(
            () => Orders.Issued().Receive((1, 2m), (2, quantity)),
            "purchase_order.receipt_quantity_not_positive",
            ViolationKind.Invalid);

    [Fact]
    public void A_quantity_finer_than_a_thousandth_is_refused() =>
        ShouldBeRefused(() => Orders.Issued().Receive((1, 0.0001m)), "purchase_order.quantity_precision", ViolationKind.Invalid);

    [Fact]
    public void A_line_named_twice_in_one_receipt_is_received_as_the_sum()
    {
        PurchaseOrder order = Orders.Issued();

        GoodsReceipt receipt = order.Receive((1, 2m), (1, 3m));

        receipt.Lines.Select(static line => (line.LineNumber, line.Quantity)).ShouldBe([(1, 5m)]);
        order.Line(1).ReceivedQuantity.ShouldBe(5m);
    }

    [Fact]
    public void Two_parts_of_the_same_line_are_checked_together_against_what_is_open() =>
        ShouldBeRefused(() => Orders.Issued().Receive((2, 3m), (2, 2m)), "purchase_order.over_receipt", ViolationKind.Conflict);

    [Fact]
    public void A_receipt_for_a_line_the_order_does_not_have_is_refused() =>
        ShouldBeRefused(() => Orders.Issued().Receive((3, 1m)), "purchase_order.unknown_line", ViolationKind.Invalid);
}
