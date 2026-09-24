using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.UnitTests;

public sealed class InvoiceMatchingTests
{
    private static readonly PurchaseOrder Order = A.IssuedOrder(A.Ordered(1, 100, 10m), A.Ordered(2, 100, 10m), A.Ordered(3, 100, 10m));

    [Fact]
    public void A_matched_invoice_becomes_payable_due_on_its_date_plus_the_suppliers_terms()
    {
        Invoice invoice = A.Invoice(A.Line(1, 5, 10m));

        invoice.Evaluate(Order, A.Received((1, 5)), paymentTermsDays: 30, A.Now);

        invoice.Status.ShouldBe(InvoiceStatus.Payable);
        invoice.MatchedAt.ShouldBe(A.Now);
        invoice.DueDate.ShouldBe(A.Today.AddDays(30));
        invoice.Reason.ShouldBe(MatchReason.None);
    }

    [Fact]
    public void A_match_before_the_supplier_is_known_leaves_the_due_date_open()
    {
        Invoice invoice = A.Invoice(A.Line(1, 5, 10m));

        invoice.Evaluate(Order, A.Received((1, 5)), paymentTermsDays: null, A.Now);

        invoice.Status.ShouldBe(InvoiceStatus.Payable);
        invoice.DueDate.ShouldBeNull();
    }

    [Fact]
    public void An_invoice_that_does_not_match_keeps_the_reason_for_people_to_read()
    {
        Invoice invoice = A.Invoice(A.Line(1, 5, 10m));

        invoice.Evaluate(Order, A.Received((1, 2)), 30, A.Now);

        invoice.Status.ShouldBe(InvoiceStatus.AwaitingReceipt);
        invoice.Reason.ShouldBe(MatchReason.QuantityExceedsReceived);
        invoice.ReasonDetail.ShouldBe("Line 1: 5 invoiced in total against 2 received.");
        invoice.MatchedAt.ShouldBeNull();
    }

    [Fact]
    public void A_waiting_invoice_matches_once_the_missing_goods_arrive()
    {
        Invoice invoice = A.Invoice(A.Line(1, 5, 10m));
        invoice.Evaluate(Order, A.Received((1, 2)), 30, A.Now);

        invoice.Evaluate(Order, A.Received((1, 5)), 30, A.Now);

        invoice.Status.ShouldBe(InvoiceStatus.Payable);
    }

    [Theory]
    [InlineData(InvoiceStatus.Payable)]
    [InlineData(InvoiceStatus.Rejected)]
    public void An_invoice_that_is_settled_either_way_is_not_matched_again(InvoiceStatus settled)
    {
        Invoice invoice = settled == InvoiceStatus.Payable ? A.Invoice(A.Line(1, 5, 10m)) : A.Invoice(A.Line(9, 5, 10m));
        invoice.Evaluate(Order, A.Received((1, 5)), 30, A.Now);
        invoice.Status.ShouldBe(settled);

        Should.Throw<InvalidOperationException>(() => invoice.Evaluate(Order, A.Received((1, 5)), 30, A.Now));
    }

    [Fact]
    public void Invoices_on_one_order_are_matched_oldest_first_and_each_counts_against_the_next()
    {
        Invoice first = CapturedAt(A.Now, A.Line(1, 6, 10m));
        Invoice second = CapturedAt(A.Now.AddMinutes(1), A.Line(1, 6, 10m));
        Invoice third = CapturedAt(A.Now.AddMinutes(2), A.Line(1, 4, 10m));
        OrderPosition position = A.Received((1, 10));

        IReadOnlyList<Invoice> matched = InvoiceMatching.Evaluate([third, second, first], Order, position, 30, A.Now);

        matched.ShouldBe([first, third]);
        second.Status.ShouldBe(InvoiceStatus.AwaitingReceipt);
        position.InvoicedOn(1).ShouldBe(10);
    }

    [Fact]
    public void An_invoice_that_does_not_match_takes_nothing_from_the_position()
    {
        OrderPosition position = A.Received((1, 10), (2, 10));

        InvoiceMatching.Evaluate([A.Invoice(A.Line(1, 5, 10m), A.Line(2, 11, 10m))], Order, position, 30, A.Now);

        position.InvoicedOn(1).ShouldBe(0);
        position.InvoicedOn(2).ShouldBe(0);
    }

    [Property(MaxTest = 300)]
    public Property However_invoices_and_receipts_interleave_no_line_is_invoiced_beyond_what_was_received() =>
        Prop.ForAll(Histories.ToArbitrary(), history =>
        {
            var position = OrderPosition.Empty();
            var captured = new List<Invoice>();
            var received = new Dictionary<int, decimal>();
            var clock = A.Now;

            foreach (Step step in history)
            {
                clock = clock.AddSeconds(1);
                if (step.Receipt is { } receipt)
                {
                    // As the receipt consumer does: record the goods, then match what was waiting.
                    position.RecordReceived(receipt);
                    foreach (ReceivedLine line in receipt.Lines)
                    {
                        received[line.LineNumber] = received.GetValueOrDefault(line.LineNumber) + line.Quantity;
                    }

                    InvoiceMatching.Evaluate(
                        captured.Where(invoice => invoice.Status == InvoiceStatus.AwaitingReceipt),
                        Order,
                        position,
                        30,
                        clock);
                }
                else
                {
                    Invoice invoice = CapturedAt(clock, step.InvoiceLines!);
                    captured.Add(invoice);
                    InvoiceMatching.Evaluate([invoice], Order, position, 30, clock);
                }

                Dictionary<int, decimal> invoiced = Matched(captured);
                foreach ((int lineNumber, decimal quantity) in invoiced)
                {
                    quantity.ShouldBeLessThanOrEqualTo(received.GetValueOrDefault(lineNumber));
                }
            }

            // Nothing is left waiting that would fit: every invoice still waiting exceeds what is left on some line.
            Dictionary<int, decimal> finallyInvoiced = Matched(captured);
            foreach (Invoice waiting in captured.Where(invoice => invoice.Status == InvoiceStatus.AwaitingReceipt))
            {
                waiting.Lines.ShouldContain(line =>
                    finallyInvoiced.GetValueOrDefault(line.LineNumber) + line.Quantity > received.GetValueOrDefault(line.LineNumber));
            }
        });

    private static Dictionary<int, decimal> Matched(IEnumerable<Invoice> invoices) =>
        invoices.Where(invoice => invoice.Status == InvoiceStatus.Payable)
            .SelectMany(invoice => invoice.Lines)
            .GroupBy(line => line.LineNumber)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

    private static Invoice CapturedAt(DateTimeOffset capturedAt, params InvoiceLine[] lines) =>
        Invoice.Capture(
            Guid.CreateVersion7(capturedAt),
            A.SupplierId,
            $"INV-{Guid.NewGuid():N}"[..20],
            A.Today,
            A.OrderId,
            lines,
            lines.Sum(line => line.Amount),
            A.Clerk,
            capturedAt);

    /// <summary>Either a receipt or an invoice; never both.</summary>
    private sealed record Step(Receipt? Receipt, InvoiceLine[]? InvoiceLines);

    private static readonly Gen<decimal> Quantity = Gen.Choose(1, 8_000).Select(thousandths => thousandths / 1000m);

    private static readonly Gen<Step> ReceiptStep =
        from lines in Gen.SubListOf(new[] { 1, 2, 3 }).Where(lines => lines.Count > 0)
        from quantities in Gen.ArrayOf(Quantity, 3)
        select new Step(
            Receipt.Record(Guid.NewGuid(), A.OrderId, lines.Select(line => new ReceivedLine(line, quantities[line - 1])), A.Now),
            null);

    private static readonly Gen<Step> InvoiceStep =
        from lines in Gen.SubListOf(new[] { 1, 2, 3 }).Where(lines => lines.Count > 0)
        from quantities in Gen.ArrayOf(Quantity, 3)
        select new Step(null, [.. lines.Select(line => A.Line(line, quantities[line - 1], 10m))]);

    private static readonly Gen<List<Step>> Histories = Gen.Frequency((1, ReceiptStep), (1, InvoiceStep)).NonEmptyListOf();
}
