using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

/// <summary>
/// The rules that are arithmetic or depend on the order things happen in, checked against a simple model over
/// generated sequences rather than a handful of hand-picked ones.
/// </summary>
public sealed class PurchaseOrderProperties
{
    [Property]
    public Property The_order_amount_is_always_the_sum_of_its_rounded_line_amounts() =>
        Prop.ForAll(Arb.From(AmendmentScenario.Generator), scenario =>
        {
            PurchaseOrder order = Orders.Draft(scenario.Lines);
            (decimal Quantity, decimal UnitPrice)[] model = [.. scenario.Lines];

            foreach ((int line, decimal quantity, decimal unitPrice) in scenario.Amendments)
            {
                order.AmendLine(People.Bruno, line, quantity, unitPrice);
                model[line - 1] = (quantity, unitPrice);
            }

            order.Lines.OrderBy(static line => line.LineNumber)
                .Select(static line => (line.Quantity, line.UnitPrice, line.Amount))
                .ShouldBe(model.Select(static line => (line.Quantity, line.UnitPrice, Amounts.Line(line.Quantity, line.UnitPrice))));
            order.Amount.ShouldBe(model.Sum(static line => Amounts.Line(line.Quantity, line.UnitPrice)));
        });

    [Property]
    public Property No_sequence_of_receipts_takes_a_line_past_what_was_ordered() =>
        Prop.ForAll(Arb.From(ReceiptScenario.Generator), scenario =>
        {
            PurchaseOrder order = Orders.Issued([.. scenario.Ordered.Select(static quantity => (quantity, 1m))]);
            decimal[] received = new decimal[scenario.Ordered.Length];

            foreach ((int Line, decimal Quantity)[] receipt in scenario.Receipts)
            {
                bool fits = receipt
                    .GroupBy(static part => part.Line)
                    .All(line => received[line.Key - 1] + line.Sum(static part => part.Quantity) <= scenario.Ordered[line.Key - 1]);

                if (!fits)
                {
                    ShouldBeRefused(() => order.Receive(receipt), "purchase_order.over_receipt", ViolationKind.Conflict);
                    continue;
                }

                order.Receive(receipt);
                foreach ((int line, decimal quantity) in receipt)
                {
                    received[line - 1] += quantity;
                }
            }

            order.Lines.OrderBy(static line => line.LineNumber).Select(static line => line.ReceivedQuantity).ShouldBe(received);
            order.Lines.ShouldAllBe(static line => line.ReceivedQuantity <= line.Quantity);
        });

    [Property]
    public Property Receipts_and_invoices_in_any_order_keep_invoiced_within_received_and_complete_exactly_when_settled() =>
        Prop.ForAll(Arb.From(FlowScenario.Generator), scenario =>
        {
            PurchaseOrder order = Orders.Issued([.. scenario.Ordered.Select(static quantity => (quantity, 1m))]);
            decimal[] received = new decimal[scenario.Ordered.Length];
            decimal[] invoiced = new decimal[scenario.Ordered.Length];
            bool completed = false;

            foreach (FlowStep step in scenario.Steps)
            {
                int index = step.Line - 1;

                if (step.IsReceipt && completed)
                {
                    ShouldBeRefused(() => order.Receive((step.Line, step.Quantity)), "purchase_order.not_issued", ViolationKind.Conflict);
                }
                else if (step.IsReceipt && received[index] + step.Quantity > scenario.Ordered[index])
                {
                    ShouldBeRefused(() => order.Receive((step.Line, step.Quantity)), "purchase_order.over_receipt", ViolationKind.Conflict);
                }
                else if (step.IsReceipt)
                {
                    order.Receive((step.Line, step.Quantity));
                    received[index] += step.Quantity;
                }
                else if (invoiced[index] + step.Quantity > received[index])
                {
                    ShouldBeRefused(
                        () => order.Invoice((step.Line, step.Quantity)),
                        "purchase_order.invoiced_exceeds_received",
                        ViolationKind.Conflict);
                }
                else
                {
                    invoiced[index] += step.Quantity;
                    bool settles = !completed && scenario.Ordered.Select((ordered, i) => ordered == received[i] && ordered == invoiced[i]).All(static settled => settled);

                    order.Invoice((step.Line, step.Quantity)).ShouldBe(settles);
                    completed |= settles;
                }

                order.Lines.ShouldAllBe(static line =>
                    line.InvoicedQuantity >= 0 && line.InvoicedQuantity <= line.ReceivedQuantity && line.ReceivedQuantity <= line.Quantity);
                (order.Status == PurchaseOrderStatus.Completed).ShouldBe(completed);
            }
        });

    [Property]
    public Property Commitment_replies_redelivered_in_any_order_leave_the_state_the_first_genuine_reply_set() =>
        Prop.ForAll(Arb.From(ReplyScenario.Generator), scenario =>
        {
            PurchaseOrder order = scenario.SendForCommitment();
            int applied = 0;

            for (int i = 0; i < scenario.Deliveries.Length; i++)
            {
                applied += scenario.Deliver(order, i) ? 1 : 0;
            }

            // Only the first delivery of the reply to the attempt in flight counts; its time and reason must
            // survive every later copy.
            int first = Array.IndexOf(scenario.Deliveries, scenario.Final);
            applied.ShouldBe(1);
            order.CommitmentAttempt.ShouldBe(scenario.Final.Attempt);

            if (scenario.Final.Committed)
            {
                order.Status.ShouldBe(PurchaseOrderStatus.Issued);
                order.IssuedAt.ShouldBe(ReplyScenario.DeliveredAt(first));
                order.IssuedBy.ShouldBe(People.Bruno.Id);
            }
            else
            {
                order.Status.ShouldBe(PurchaseOrderStatus.Draft);
                order.CommitmentRejectionReason.ShouldBe(ReplyScenario.Reason(first));
                order.IssuedBy.ShouldBeNull();
            }
        });

    [Property]
    public Property However_replies_and_a_cancel_interleave_the_order_ends_cancelled_and_ignores_what_comes_after() =>
        Prop.ForAll(Arb.From(ReplyScenario.Generator.SelectMany(static s => Gen.Choose(0, s.Deliveries.Length), static (s, at) => (Scenario: s, CancelAt: at))), input =>
        {
            PurchaseOrder order = input.Scenario.SendForCommitment();

            for (int i = 0; i < input.Scenario.Deliveries.Length; i++)
            {
                if (i == input.CancelAt)
                {
                    order.Cancel(People.Beth, Orders.Now);
                }

                bool applied = input.Scenario.Deliver(order, i);
                if (i >= input.CancelAt)
                {
                    applied.ShouldBeFalse();
                }
            }

            if (input.CancelAt == input.Scenario.Deliveries.Length)
            {
                order.Cancel(People.Beth, Orders.Now);
            }

            order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        });

    // Thousandths and ten-thousandths, so every generated value fits the scale its column allows.
    private static Gen<decimal> Quantity(int maxThousandths) =>
        Gen.Choose(0, maxThousandths).Select(static n => n / 1000m);

    private static Gen<decimal> PositiveQuantity(int maxThousandths) =>
        Gen.Choose(1, maxThousandths).Select(static n => n / 1000m);

    private static readonly Gen<decimal> UnitPrice = Gen.Choose(0, 10_000_000).Select(static n => n / 10_000m);

    private static readonly Gen<(decimal Quantity, decimal UnitPrice)> PricedQuantity =
        Gen.Zip(Quantity(50_000), UnitPrice, static (quantity, price) => (quantity, price));

    private static Gen<decimal[]> OrderedQuantities(Gen<decimal> quantity) =>
        Gen.Choose(1, 4)
            .SelectMany(count => Gen.ArrayOf(quantity, count))
            .Where(static quantities => quantities.Any(static q => q > 0));

    private static string Show<T>(IEnumerable<T> items) => "[" + string.Join(", ", items) + "]";

    public sealed record AmendmentScenario(
        (decimal Quantity, decimal UnitPrice)[] Lines, (int Line, decimal Quantity, decimal UnitPrice)[] Amendments)
    {
        public static readonly Gen<AmendmentScenario> Generator =
            from lines in Gen.Choose(1, 5).SelectMany(count => Gen.ArrayOf(PricedQuantity, count))
            from amendments in Gen.ArrayOf(
                Gen.Zip(Gen.Choose(1, lines.Length), PricedQuantity, static (line, value) => (line, value.Quantity, value.UnitPrice)))
            select new AmendmentScenario(lines, amendments);

        public override string ToString() => $"lines {Show(Lines)}, amendments {Show(Amendments)}";
    }

    public sealed record ReceiptScenario(decimal[] Ordered, (int Line, decimal Quantity)[][] Receipts)
    {
        public static readonly Gen<ReceiptScenario> Generator =
            from ordered in OrderedQuantities(Quantity(20_000))
            from receipts in Gen.ArrayOf(
                Gen.Choose(1, 3).SelectMany(count => Gen.ArrayOf(
                    Gen.Zip(Gen.Choose(1, ordered.Length), PositiveQuantity(8_000), static (line, quantity) => (line, quantity)),
                    count)))
            select new ReceiptScenario(ordered, receipts);

        public override string ToString() =>
            $"ordered {Show(Ordered)}, receipts {Show(Receipts.Select(static receipt => Show(receipt)))}";
    }

    public sealed record FlowStep(bool IsReceipt, int Line, decimal Quantity)
    {
        public override string ToString() => $"{(IsReceipt ? "receive" : "invoice")} {Quantity} on {Line}";
    }

    public sealed record FlowScenario(decimal[] Ordered, FlowStep[] Steps)
    {
        // Whole units in small numbers, so generated sequences often settle every line and complete the order.
        public static readonly Gen<FlowScenario> Generator =
            from ordered in OrderedQuantities(Gen.Choose(0, 4).Select(static n => (decimal)n))
            from steps in Gen.ArrayOf(
                from isReceipt in Gen.Elements(true, false)
                from line in Gen.Choose(1, ordered.Length)
                from quantity in Gen.Choose(1, 3)
                select new FlowStep(isReceipt, line, quantity))
            select new FlowScenario(ordered, steps);

        public override string ToString() => $"ordered {Show(Ordered)}, steps {Show(Steps)}";
    }

    public sealed record Reply(bool Committed, int Attempt)
    {
        public override string ToString() => $"{(Committed ? "committed" : "rejected")}#{Attempt}";
    }

    /// <summary>
    /// An order refused <paramref name="EarlierRejections"/> times and now waiting on the next attempt, and a
    /// delivery of every genuine reply one to three times plus replies to attempts Budgets never answered that
    /// way, all shuffled.
    /// </summary>
    public sealed record ReplyScenario(int EarlierRejections, Reply Final, Reply[] Deliveries)
    {
        public static readonly Gen<ReplyScenario> Generator =
            from earlier in Gen.Choose(0, 3)
            from committed in Gen.Elements(true, false)
            let final = new Reply(committed, earlier + 1)
            let genuine = Enumerable.Range(1, earlier).Select(static attempt => new Reply(false, attempt)).Append(final).ToArray()
            from copies in Gen.ArrayOf(Gen.Choose(1, 3), genuine.Length)
            from strays in Gen.ArrayOf(
                Gen.Zip(Gen.Elements(true, false), Gen.Choose(1, earlier + 3), static (c, attempt) => new Reply(c, attempt))
                    .Where(reply => reply.Attempt != final.Attempt))
            from deliveries in Gen.Shuffle(genuine.SelectMany((reply, i) => Enumerable.Repeat(reply, copies[i])).Concat(strays))
            select new ReplyScenario(earlier, final, deliveries);

        public static DateTimeOffset DeliveredAt(int index) => Orders.Now.AddSeconds(index);

        public static string Reason(int index) => $"insufficient_funds ({index})";

        public PurchaseOrder SendForCommitment()
        {
            PurchaseOrder order = Orders.Draft();
            Supplier supplier = Orders.ActiveSupplierOf(order);

            for (int attempt = 1; attempt <= EarlierRejections; attempt++)
            {
                order.RequestIssue(People.Bruno, supplier);
                order.RejectCommitment(attempt, "insufficient_funds");
            }

            order.RequestIssue(People.Bruno, supplier);
            return order;
        }

        public bool Deliver(PurchaseOrder order, int index)
        {
            Reply reply = Deliveries[index];
            return reply.Committed
                ? order.ConfirmCommitment(reply.Attempt, DeliveredAt(index))
                : order.RejectCommitment(reply.Attempt, Reason(index));
        }

        public override string ToString() =>
            $"{EarlierRejections} earlier rejections, final {Final}, deliveries {Show(Deliveries)}";
    }
}
