using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.UnitTests;

public sealed class RepeatedReceiptTests
{
    private static (PurchaseOrder Order, GoodsReceipt Receipt) Recorded()
    {
        PurchaseOrder order = Orders.Issued();
        GoodsReceipt receipt = order.Receive((1, 4m), (2, 1.5m));
        return (order, receipt);
    }

    [Fact]
    public void The_same_order_person_and_quantities_in_any_order_is_a_repeat()
    {
        (PurchaseOrder order, GoodsReceipt receipt) = Recorded();

        receipt.IsRepeatedBy(order.Id, People.Rosa.Id, [new LineQuantity(2, 1.500m), new LineQuantity(1, 4m)]).ShouldBeTrue();
    }

    [Fact]
    public void A_line_split_into_two_entries_that_add_up_is_a_repeat()
    {
        (PurchaseOrder order, GoodsReceipt receipt) = Recorded();

        receipt.IsRepeatedBy(order.Id, People.Rosa.Id, [new LineQuantity(1, 1m), new LineQuantity(1, 3m), new LineQuantity(2, 1.5m)])
            .ShouldBeTrue();
    }

    [Fact]
    public void A_different_quantity_is_not_a_repeat()
    {
        (PurchaseOrder order, GoodsReceipt receipt) = Recorded();

        receipt.IsRepeatedBy(order.Id, People.Rosa.Id, [new LineQuantity(1, 4m), new LineQuantity(2, 1m)]).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_or_extra_line_is_not_a_repeat()
    {
        (PurchaseOrder order, GoodsReceipt receipt) = Recorded();

        receipt.IsRepeatedBy(order.Id, People.Rosa.Id, [new LineQuantity(1, 4m)]).ShouldBeFalse();
    }

    [Fact]
    public void Another_order_or_another_person_is_not_a_repeat()
    {
        (PurchaseOrder order, GoodsReceipt receipt) = Recorded();
        LineQuantity[] same = [new LineQuantity(1, 4m), new LineQuantity(2, 1.5m)];

        receipt.IsRepeatedBy(Guid.CreateVersion7(), People.Rosa.Id, same).ShouldBeFalse();
        receipt.IsRepeatedBy(order.Id, People.Beth.Id, same).ShouldBeFalse();
    }

    [Fact]
    public void A_receipt_cannot_have_the_empty_id() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => Orders.Issued().RecordReceipt(People.Rosa, Guid.Empty, [new LineQuantity(1, 1m)], Orders.Now));
}
