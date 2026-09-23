using FsCheck;
using FsCheck.Fluent;

namespace Matchbook.Budgets.UnitTests.Funds;

/// <summary>
/// A budget and the funds messages about it, in the order they were first delivered, plus the messages the
/// broker delivered a second time. Each redelivery repeats message <c>Original</c> right after delivery number
/// <c>After</c>, which is never before the original.
/// </summary>
public sealed class FundsHistory(
    decimal allotment, IReadOnlyList<FundsMessage> deliveries, IReadOnlyList<(int Original, int After)> redeliveries)
{
    public decimal Allotment => allotment;

    public IReadOnlyList<FundsMessage> Deliveries => deliveries;

    public IEnumerable<FundsMessage> WithRedeliveries()
    {
        for (int i = 0; i < deliveries.Count; i++)
        {
            yield return deliveries[i];
            foreach ((int original, int after) in redeliveries)
            {
                if (after == i)
                {
                    yield return deliveries[original];
                }
            }
        }
    }

    public override string ToString() =>
        $"allotment {allotment}: {string.Join(", ", WithRedeliveries())}";
}

/// <summary>
/// Committed orders, and their invoices and closes in two independent orders, each with the same redeliveries.
/// </summary>
public sealed class SettlementCase(
    IReadOnlyList<decimal> orderAmounts, IReadOnlyList<FundsMessage> first, IReadOnlyList<FundsMessage> second)
{
    public IReadOnlyList<FundsMessage> First => first;

    public IReadOnlyList<FundsMessage> Second => second;

    public decimal InvoicedTotal => first.OfType<MatchInvoice>().Distinct().Sum(invoice => invoice.Amount);

    /// <summary>A budget exactly big enough for every order, with every order reserved and committed.</summary>
    public FundsSimulation Committed()
    {
        var run = new FundsSimulation(orderAmounts.Sum());
        for (int order = 1; order <= orderAmounts.Count; order++)
        {
            run.Deliver(new Submit(order, orderAmounts[order - 1]));
            run.Deliver(new RequestCommitment(order, order, Attempt: 1, orderAmounts[order - 1]));
        }

        return run;
    }

    public override string ToString() =>
        $"orders {string.Join(", ", orderAmounts)}; first: {string.Join(", ", first)}; second: {string.Join(", ", second)}";
}

public static class FundsGenerators
{
    public static Gen<FundsHistory> History =>
        from requisitions in Gen.Choose(1, 6)
        from allotment in Euros(100, 2_000)
        from stories in Gen.CollectToArray(Enumerable.Range(1, requisitions), Story)
        from deliveries in Gen.Shuffle(stories.SelectMany(story => story))
        from redeliveries in Redeliveries(deliveries.Length)
        select new FundsHistory(allotment, deliveries, redeliveries);

    public static Gen<SettlementCase> Settlement =>
        from orders in Gen.Choose(1, 5)
        from amounts in Gen.ArrayOf(Euros(1, 500), orders)
        from invoices in Gen.CollectToArray(Enumerable.Range(1, orders), Invoices)
        let block = invoices.SelectMany(i => i).Concat(Enumerable.Range(1, orders).Select(Close)).ToArray()
        from redelivered in Gen.SubListOf(block)
        from first in Gen.Shuffle(block.Concat(redelivered))
        from second in Gen.Shuffle(block.Concat(redelivered))
        select new SettlementCase(amounts, first, second);

    private static Gen<decimal> Euros(int from, int to) => Gen.Choose(from * 100, to * 100).Select(cents => cents / 100m);

    /// <summary>
    /// One requisition's life as its publishers would send it: submitted, then either withdrawn, or ordered with
    /// one to three commitment attempts (each for its own amount, as a buyer edits prices between them), up to
    /// three invoices, and usually a close. The order and invoice numbers derive from the requisition's, so no
    /// two stories share a document.
    /// </summary>
    private static Gen<FundsMessage[]> Story(int requisition) =>
        from amount in Euros(1, 500)
        from ordered in Gen.Frequency((1, Gen.Constant(false)), (3, Gen.Constant(true)))
        from attempts in Gen.Choose(1, 3).SelectMany(count => Gen.ArrayOf(Euros(1, 600), count))
        from invoices in Invoices(requisition)
        from closed in Gen.Frequency((3, Gen.Constant(true)), (1, Gen.Constant(false)))
        select ordered
            ? [
                new Submit(requisition, amount),
                .. attempts.Select((attemptAmount, i) => new RequestCommitment(requisition, requisition, i + 1, attemptAmount)),
                .. invoices,
                .. closed ? [Close(requisition)] : Array.Empty<FundsMessage>(),
            ]
            : new FundsMessage[] { new Submit(requisition, amount), new Release(requisition) };

    private static Gen<FundsMessage[]> Invoices(int order) =>
        Gen.Choose(0, 3)
            .SelectMany(count => Gen.ArrayOf(Euros(1, 400), count))
            .Select(amounts => amounts.Select((amount, i) => (FundsMessage)new MatchInvoice((order * 10) + i, order, amount)).ToArray());

    private static FundsMessage Close(int order) => new CloseOrder(order, order);

    private static Gen<(int Original, int After)[]> Redeliveries(int deliveries) =>
        from count in Gen.Choose(0, deliveries)
        from originals in Gen.ArrayOf(Gen.Choose(0, deliveries - 1), count)
        from pairs in Gen.CollectToArray(originals, original => Gen.Choose(original, deliveries - 1).Select(after => (original, after)))
        select pairs;
}

public static class FundsArbitraries
{
    public static Arbitrary<FundsHistory> History() => Arb.From(FundsGenerators.History);

    public static Arbitrary<SettlementCase> Settlement() => Arb.From(FundsGenerators.Settlement);
}
