using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.UnitTests;

public sealed class ThreeWayMatchTests
{
    private static readonly PurchaseOrder Order = A.IssuedOrder(A.Ordered(1, 10, 10m), A.Ordered(2, 5, 200m));

    [Fact]
    public void An_invoice_within_what_was_received_and_within_tolerance_matches()
    {
        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 10, 10m)), Order, A.Received((1, 10)));

        result.Outcome.ShouldBe(MatchOutcome.Matched);
        result.Reason.ShouldBe(MatchReason.None);
    }

    [Fact]
    public void An_invoice_for_an_order_not_yet_known_waits_for_it()
    {
        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10m)), null, OrderPosition.Empty());

        result.Outcome.ShouldBe(MatchOutcome.AwaitingPurchaseOrder);
        result.Reason.ShouldBe(MatchReason.OrderUnknown);
    }

    [Fact]
    public void An_order_only_mentioned_by_receipts_so_far_counts_as_not_yet_known() =>
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10m)), PurchaseOrder.FirstMentioned(A.OrderId), A.Received((1, 5)))
            .Outcome.ShouldBe(MatchOutcome.AwaitingPurchaseOrder);

    [Fact]
    public void An_invoice_for_a_cancelled_order_is_rejected()
    {
        PurchaseOrder order = A.IssuedOrder(A.Ordered(1, 10, 10m));
        order.RecordClosure("Cancelled", cancelled: true, A.Now);

        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10m)), order, OrderPosition.Empty());

        result.Outcome.ShouldBe(MatchOutcome.Rejected);
        result.Reason.ShouldBe(MatchReason.OrderCancelled);
    }

    [Fact]
    public void A_cancellation_that_arrived_before_the_order_still_rejects_the_invoice()
    {
        PurchaseOrder order = PurchaseOrder.FirstMentioned(A.OrderId);
        order.RecordClosure("Cancelled", cancelled: true, A.Now);

        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10m)), order, OrderPosition.Empty()).Reason.ShouldBe(MatchReason.OrderCancelled);
    }

    [Fact]
    public void An_order_closed_short_still_matches_what_was_received()
    {
        PurchaseOrder order = A.IssuedOrder(A.Ordered(1, 10, 10m));
        order.RecordClosure("ShortClosed", cancelled: false, A.Now);

        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 4, 10m)), order, A.Received((1, 4))).Outcome.ShouldBe(MatchOutcome.Matched);
    }

    [Fact]
    public void An_invoice_from_a_supplier_other_than_the_orders_is_rejected()
    {
        Invoice invoice = A.Invoice("INV-1", A.Today, A.OtherSupplierId, A.Clerk, A.Line(1, 1, 10m));

        MatchResult result = ThreeWayMatch.Evaluate(invoice, Order, A.Received((1, 10)));

        result.Outcome.ShouldBe(MatchOutcome.Rejected);
        result.Reason.ShouldBe(MatchReason.SupplierMismatch);
    }

    [Fact]
    public void An_invoice_billing_a_line_the_order_does_not_have_is_rejected()
    {
        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10m), A.Line(7, 1, 10m)), Order, A.Received((1, 10)));

        result.Outcome.ShouldBe(MatchOutcome.Rejected);
        result.Reason.ShouldBe(MatchReason.LineNotOnOrder);
        result.Detail.ShouldContain("line 7");
    }

    [Fact]
    public void More_invoiced_than_received_waits_for_goods()
    {
        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 10, 10m)), Order, A.Received((1, 9.999m)));

        result.Outcome.ShouldBe(MatchOutcome.AwaitingReceipt);
        result.Reason.ShouldBe(MatchReason.QuantityExceedsReceived);
    }

    [Fact]
    public void Quantities_already_matched_on_the_line_count_against_what_was_received()
    {
        OrderPosition position = A.Position(received: [(1, 10)], invoiced: [(1, 6)]);

        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 4, 10m)), Order, position).Outcome.ShouldBe(MatchOutcome.Matched);
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 5, 10m)), Order, position).Outcome.ShouldBe(MatchOutcome.AwaitingReceipt);
    }

    [Fact]
    public void Goods_received_on_another_line_do_not_count() =>
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10m)), Order, A.Received((2, 5))).Outcome.ShouldBe(MatchOutcome.AwaitingReceipt);

    [Fact]
    public void A_price_beyond_tolerance_is_a_price_variance_exception()
    {
        // 2% of 100.00 is 2.00; 10 x 0.21 is 2.10.
        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 10, 10.21m)), Order, A.Received((1, 10)));

        result.Outcome.ShouldBe(MatchOutcome.PriceVariance);
        result.Reason.ShouldBe(MatchReason.PriceVarianceBeyondTolerance);
        result.Detail.ShouldBe("Line 1: unit price 10.21 against 10.00 ordered, a variance of 2.10 where 2.00 is allowed.");
    }

    [Fact]
    public void A_price_below_the_order_by_more_than_tolerance_is_a_variance_too() =>
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 10, 9.79m)), Order, A.Received((1, 10))).Outcome.ShouldBe(MatchOutcome.PriceVariance);

    [Fact]
    public void A_variance_of_exactly_two_percent_is_within_tolerance() =>
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 10, 10.20m)), Order, A.Received((1, 10))).Outcome.ShouldBe(MatchOutcome.Matched);

    [Fact]
    public void On_a_large_line_the_allowance_stops_at_one_hundred()
    {
        // 1,000 x 200.00 is 200,000.00, and 2% of that is 4,000.00, so the cap binds: 0.10 a unit over is exactly 100.00.
        PurchaseOrder order = A.IssuedOrder(A.Ordered(1, 1000, 200m));

        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1000, 200.10m)), order, A.Received((1, 1000))).Outcome.ShouldBe(MatchOutcome.Matched);
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1000, 200.1001m)), order, A.Received((1, 1000))).Outcome.ShouldBe(MatchOutcome.PriceVariance);
    }

    [Fact]
    public void One_ten_thousandth_of_a_euro_over_the_cap_is_a_variance()
    {
        // One unit at 10,000.00: 2% would be 200.00, so the cap of 100.00 is the limit, to the last decimal.
        PurchaseOrder order = A.IssuedOrder(A.Ordered(1, 1, 10_000m));

        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10_100m)), order, A.Received((1, 1))).Outcome.ShouldBe(MatchOutcome.Matched);
        ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 1, 10_100.0001m)), order, A.Received((1, 1))).Outcome.ShouldBe(MatchOutcome.PriceVariance);
    }

    [Fact]
    public void Goods_are_checked_before_price_so_an_approver_only_sees_invoices_that_could_otherwise_be_paid()
    {
        MatchResult result = ThreeWayMatch.Evaluate(A.Invoice(A.Line(1, 10, 11m)), Order, A.Received((1, 5)));

        result.Outcome.ShouldBe(MatchOutcome.AwaitingReceipt);
        result.Lines.Single().PriceWithinTolerance.ShouldBeFalse();
    }

    [Fact]
    public void Every_line_is_measured_and_reported_once_the_order_is_known()
    {
        MatchResult result = ThreeWayMatch.Evaluate(
            A.Invoice(A.Line(1, 4, 10m), A.Line(2, 5, 201m)),
            Order,
            A.Position(received: [(1, 10), (2, 5)], invoiced: [(1, 3)]));

        result.Lines.ShouldBe(
        [
            new LineFinding(1, 4, 3, 10, 10m, 10m, 0m, 0.8m),
            new LineFinding(2, 5, 0, 5, 200m, 201m, 5m, 20m),
        ]);
    }

    [Fact]
    public void The_match_changes_neither_the_invoice_nor_the_position()
    {
        Invoice invoice = A.Invoice(A.Line(1, 10, 10m));
        OrderPosition position = A.Received((1, 10));

        ThreeWayMatch.Evaluate(invoice, Order, position);

        invoice.Status.ShouldBe(InvoiceStatus.Captured);
        position.InvoicedOn(1).ShouldBe(0);
    }
}
