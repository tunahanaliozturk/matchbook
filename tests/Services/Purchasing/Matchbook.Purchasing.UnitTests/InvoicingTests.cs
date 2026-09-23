using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

public sealed class InvoicingTests
{
    private static readonly DateTimeOffset Later = Orders.Now.AddDays(3);

    [Fact]
    public void A_matched_invoice_counts_as_invoiced_and_a_partly_invoiced_order_stays_issued()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 10m), (2, 4m));

        order.Invoice((1, 10m), (2, 1m)).ShouldBeFalse();

        order.Line(1).InvoicedQuantity.ShouldBe(10m);
        order.Line(2).InvoicedQuantity.ShouldBe(1m);
        order.Status.ShouldBe(PurchaseOrderStatus.Issued);
    }

    [Fact]
    public void The_order_completes_when_every_line_is_received_and_invoiced_in_full()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 10m), (2, 4m));
        order.Invoice((1, 10m));

        order.RecordInvoice([new LineQuantity(2, 4m)], Later).ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Completed);
        order.ClosedAt.ShouldBe(Later);
        order.ClosedBy.ShouldBeNull();
    }

    [Fact]
    public void A_fully_received_order_waits_for_its_invoices_before_completing()
    {
        PurchaseOrder order = Orders.Issued();

        order.Receive((1, 10m), (2, 4m));

        order.Status.ShouldBe(PurchaseOrderStatus.Issued);
    }

    [Fact]
    public void A_line_ordered_at_zero_does_not_hold_up_completion()
    {
        PurchaseOrder order = Orders.Draft();
        order.AmendLine(People.Bruno, 2, 0m, 99.99m);
        order.RequestIssue(People.Bruno, Orders.ActiveSupplierOf(order));
        order.ConfirmCommitment(1, Orders.Now);
        order.Receive((1, 10m));

        order.Invoice((1, 10m)).ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Completed);
    }

    [Fact]
    public void An_invoice_for_more_than_was_received_is_refused_and_no_line_of_it_is_counted()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 5m), (2, 4m));
        order.Invoice((1, 3m));

        ShouldBeRefused(
            () => order.Invoice((2, 4m), (1, 2.001m)), "purchase_order.invoiced_exceeds_received", ViolationKind.Conflict);

        order.Line(1).InvoicedQuantity.ShouldBe(3m);
        order.Line(2).InvoicedQuantity.ShouldBe(0m);
    }

    [Fact]
    public void Invoice_lines_for_the_same_order_line_are_added_together()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 5m));

        order.Invoice((1, 2m), (1, 3m));

        order.Line(1).InvoicedQuantity.ShouldBe(5m);
    }

    [Fact]
    public void A_short_closed_order_still_counts_invoices_for_what_it_received_but_stays_short_closed()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 10m), (2, 3m));
        order.ShortClose(People.Bruno, Orders.Now);

        order.Invoice((1, 10m), (2, 3m)).ShouldBeFalse();

        order.Status.ShouldBe(PurchaseOrderStatus.ShortClosed);
        order.Line(2).InvoicedQuantity.ShouldBe(3m);
    }

    [Fact]
    public void An_invoice_against_an_order_with_nothing_received_is_refused() =>
        ShouldBeRefused(
            () => Orders.Issued().Invoice((1, 1m)), "purchase_order.invoiced_exceeds_received", ViolationKind.Conflict);

    [Fact]
    public void A_negative_invoiced_quantity_is_refused()
    {
        PurchaseOrder order = Orders.Issued();
        order.Receive((1, 5m));

        ShouldBeRefused(() => order.Invoice((1, 5m), (1, -1m)), "purchase_order.negative_quantity", ViolationKind.Invalid);
    }

    [Fact]
    public void An_invoice_for_a_line_the_order_does_not_have_is_refused() =>
        ShouldBeRefused(() => Orders.Issued().Invoice((9, 1m)), "purchase_order.unknown_line", ViolationKind.Invalid);
}
