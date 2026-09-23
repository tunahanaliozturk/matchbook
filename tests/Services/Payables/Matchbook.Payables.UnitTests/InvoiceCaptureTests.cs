using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

public sealed class InvoiceCaptureTests
{
    [Fact]
    public void A_captured_invoice_keeps_what_was_billed_and_who_captured_it()
    {
        Invoice invoice = Capture([A.Line(2, 3, 12.5m), A.Line(1, 10, 4.1234m)], total: 78.73m);

        invoice.Status.ShouldBe(InvoiceStatus.Captured);
        invoice.Total.ShouldBe(78.73m);
        invoice.Lines.Select(line => line.LineNumber).ShouldBe([1, 2]);
        invoice.Number.ShouldBe("INV-0042");
        invoice.NormalisedNumber.ShouldBe("INV42");
        invoice.CapturedBy.ShouldBe(A.Clerk.Id);
        invoice.CapturedAt.ShouldBe(A.Now);
    }

    [Fact]
    public void Line_amounts_are_rounded_half_away_from_zero_like_everywhere_else() =>
        A.Line(1, 1.5m, 0.0333m).Amount.ShouldBe(0.05m);

    [Fact]
    public void The_declared_total_must_be_the_sum_of_the_line_amounts() =>
        Refusal(() => Capture([A.Line(1, 3, 10m), A.Line(2, 1, 0.005m)], total: 30.00m)).ShouldBe("invoice.total_mismatch");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void The_total_must_be_above_zero_in_cents(double total) =>
        Refusal(() => Capture([A.Line(1, 1, 1m)], total: (decimal)total)).ShouldBe("invoice.total_invalid");

    [Fact]
    public void An_invoice_needs_at_least_one_line() =>
        Refusal(() => Capture([], total: 1m)).ShouldBe("invoice.lines_required");

    [Fact]
    public void An_invoice_has_at_most_fifty_lines()
    {
        InvoiceLine[] lines = [.. Enumerable.Range(1, 51).Select(number => A.Line(number, 1, 1m))];

        Refusal(() => Capture(lines, total: 51m)).ShouldBe("invoice.too_many_lines");
    }

    [Fact]
    public void An_order_line_cannot_be_billed_twice_on_one_invoice() =>
        Refusal(() => Capture([A.Line(1, 1, 1m), A.Line(1, 2, 1m)], total: 3m)).ShouldBe("invoice.duplicate_line");

    [Fact]
    public void Line_numbers_start_at_one() =>
        Refusal(() => Capture([A.Line(0, 1, 1m)], total: 1m)).ShouldBe("invoice.line_number_invalid");

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(1.0005)]
    public void A_quantity_must_be_above_zero_with_at_most_three_decimals(double quantity) =>
        Refusal(() => Capture([A.Line(1, (decimal)quantity, 1m)], total: 1m)).ShouldBe("invoice.quantity_invalid");

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.00001)]
    public void A_unit_price_cannot_be_negative_or_have_more_than_four_decimals(double unitPrice) =>
        Refusal(() => Capture([A.Line(1, 1, (decimal)unitPrice)], total: 1m)).ShouldBe("invoice.unit_price_invalid");

    [Fact]
    public void A_line_at_a_price_of_zero_is_allowed_beside_one_that_is_not()
    {
        Invoice invoice = Capture([A.Line(1, 1, 0m), A.Line(2, 1, 5m)], total: 5m);

        invoice.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public void An_invoice_needs_a_supplier_and_an_order()
    {
        Refusal(() => Invoice.Capture(Guid.NewGuid(), Guid.Empty, "INV-1", A.Today, A.OrderId, [A.Line(1, 1, 1m)], 1m, A.Clerk, A.Now))
            .ShouldBe("invoice.supplier_required");
        Refusal(() => Invoice.Capture(Guid.NewGuid(), A.SupplierId, "INV-1", A.Today, Guid.Empty, [A.Line(1, 1, 1m)], 1m, A.Clerk, A.Now))
            .ShouldBe("invoice.purchase_order_required");
    }

    [Fact]
    public void A_refusal_of_what_was_captured_is_a_422() =>
        Should.Throw<BusinessRuleException>(() => Capture([A.Line(1, 1, 1m)], total: 2m)).Kind.ShouldBe(ViolationKind.Invalid);

    private static Invoice Capture(InvoiceLine[] lines, decimal total) =>
        Invoice.Capture(Guid.NewGuid(), A.SupplierId, "INV-0042", A.Today, A.OrderId, lines, total, A.Clerk, A.Now);

    private static string Refusal(Action capture) => Should.Throw<BusinessRuleException>(capture).Code;
}
