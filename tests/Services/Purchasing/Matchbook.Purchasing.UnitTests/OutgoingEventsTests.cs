using Matchbook.Contracts.Purchasing;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.UnitTests;

public sealed class OutgoingEventsTests
{
    [Fact]
    public void A_commitment_request_carries_the_attempt_in_flight_and_the_amount_as_it_stands()
    {
        PurchaseOrder order = Orders.Pending();
        order.RejectCommitment(1, "insufficient_funds");
        order.AmendLine(People.Bruno, 1, 2m, 12.50m);
        order.RequestIssue(People.Bruno, Orders.ActiveSupplierOf(order));

        PurchaseOrderCommitmentRequested request = OutgoingEvents.CommitmentRequested(order, Orders.Now);

        request.Attempt.ShouldBe(2);
        request.Amount.ShouldBe(25.00m + 399.96m);
        request.PurchaseOrderId.ShouldBe(order.Id);
        request.RequisitionId.ShouldBe(order.RequisitionId);
        request.CostCentreCode.ShouldBe(order.CostCentreCode);
        request.FiscalYear.ShouldBe(order.FiscalYear);
    }

    [Fact]
    public void Issued_carries_the_buyer_who_issued_it_and_every_line_in_line_order()
    {
        PurchaseOrder order = Orders.Issued();

        PurchaseOrderIssued issued = OutgoingEvents.Issued(order);

        issued.IssuedBy.ShouldBe(People.Bruno.Id);
        issued.Number.ShouldBe(order.Number);
        issued.SupplierId.ShouldBe(order.SupplierId);
        issued.Amount.ShouldBe(524.96m);
        issued.OccurredAt.ShouldBe(Orders.Now);
        issued.Lines.ShouldBe(
        [
            new PurchaseOrderLine(1, "Item 1", 10m, "each", 12.50m, 125.00m),
            new PurchaseOrderLine(2, "Item 2", 4m, "each", 99.99m, 399.96m),
        ]);
    }

    [Fact]
    public void Goods_received_carries_this_receipts_quantities_not_the_running_totals()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 6m));
        GoodsReceipt second = order.Receive((1, 3m), (2, 4m));

        GoodsReceived received = OutgoingEvents.GoodsReceived(second);

        received.ReceiptId.ShouldBe(second.Id);
        received.ReceivedBy.ShouldBe(People.Rosa.Id);
        received.Lines.ShouldBe([new ReceiptLine(1, 3m), new ReceiptLine(2, 4m)]);
    }

    [Theory]
    [InlineData("completed", PurchaseOrderCloseReason.Completed)]
    [InlineData("short-closed", PurchaseOrderCloseReason.ShortClosed)]
    [InlineData("cancelled", PurchaseOrderCloseReason.Cancelled)]
    public void Closed_names_the_way_the_order_ended(string state, string reason)
    {
        PurchaseOrder order = Orders.InState(state);

        PurchaseOrderClosed closed = OutgoingEvents.Closed(order);

        closed.Reason.ShouldBe(reason);
        closed.RequisitionId.ShouldBe(order.RequisitionId);
        closed.OccurredAt.ShouldBe(Orders.Now);
    }

    [Fact]
    public void An_open_order_has_no_closed_event_to_publish() =>
        Should.Throw<InvalidOperationException>(() => OutgoingEvents.Closed(Orders.Issued()));
}
