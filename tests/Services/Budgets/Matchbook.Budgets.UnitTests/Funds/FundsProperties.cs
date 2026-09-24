using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.UnitTests.Funds;

/// <summary>
/// The funds rules over random histories: several requisitions against one budget, each submitted and then
/// withdrawn or ordered, with retried commitment attempts, invoices over and under their order, closes, every
/// message shuffled into any order, and some delivered twice.
/// </summary>
public sealed class FundsProperties
{
    private const int Histories = 3_000;

    [Property(Arbitrary = [typeof(FundsArbitraries)], MaxTest = Histories)]
    public void Every_figure_equals_the_sum_of_its_ledger(FundsHistory history)
    {
        FundsSimulation run = new FundsSimulation(history.Allotment).Deliver(history.WithRedeliveries());

        run.Budget.Allotted.ShouldBe(run.Ledger.Sum(e => e.Allotted));
        run.Budget.Reserved.ShouldBe(run.Ledger.Sum(e => e.Reserved));
        run.Budget.Committed.ShouldBe(run.Ledger.Sum(e => e.Committed));
        run.Budget.Actual.ShouldBe(run.Ledger.Sum(e => e.Actual));
    }

    [Property(Arbitrary = [typeof(FundsArbitraries)], MaxTest = Histories)]
    public void Replaying_the_ledger_never_takes_available_below_zero_on_a_reservation_or_commitment(FundsHistory history)
    {
        FundsSimulation run = new FundsSimulation(history.Allotment).Deliver(history.WithRedeliveries());

        decimal available = 0m;
        foreach (LedgerEntry entry in run.Ledger)
        {
            available -= entry.Movement.Consumes;
            if (entry.Step is LedgerStep.Reserve or LedgerStep.Commit)
            {
                available.ShouldBeGreaterThanOrEqualTo(0m, $"after {entry.Step} of {entry.DocumentId}");
            }
        }
    }

    [Property(Arbitrary = [typeof(FundsArbitraries)], MaxTest = Histories)]
    public void Delivering_any_message_a_second_time_changes_nothing(FundsHistory history)
    {
        string once = new FundsSimulation(history.Allotment).Deliver(history.Deliveries).State();

        string twice = new FundsSimulation(history.Allotment).Deliver(history.WithRedeliveries()).State();

        twice.ShouldBe(once);
    }

    [Property(Arbitrary = [typeof(FundsArbitraries)], MaxTest = Histories)]
    public void The_figures_agree_with_the_documents_behind_them(FundsHistory history)
    {
        FundsSimulation run = new FundsSimulation(history.Allotment).Deliver(history.WithRedeliveries());
        Dictionary<int, decimal> invoicedPerOrder = history.Deliveries
            .OfType<MatchInvoice>()
            .Where(invoice => run.Orders.TryGetValue(FundsSimulation.OrderId(invoice.Order), out OrderCommitment? order) && order.WasCommitted)
            .GroupBy(invoice => invoice.Order)
            .ToDictionary(group => group.Key, group => group.Sum(invoice => invoice.Amount));

        run.Budget.Reserved.ShouldBe(run.Reservations.Values.Sum(r => r.Held));
        run.Budget.Committed.ShouldBe(run.Orders.Values.Sum(o => o.Remaining));
        run.Budget.Actual.ShouldBe(invoicedPerOrder.Values.Sum());
        foreach ((int number, decimal invoiced) in invoicedPerOrder)
        {
            OrderCommitment order = run.Orders[FundsSimulation.OrderId(number)];
            order.Remaining.ShouldBe(order.Status == CommitmentStatus.Closed ? 0m : Math.Max(0m, order.Amount - invoiced));
        }
    }

    [Property(Arbitrary = [typeof(FundsArbitraries)], MaxTest = Histories)]
    public void Invoices_and_closes_end_in_the_same_place_whatever_their_order(SettlementCase settlement)
    {
        FundsSimulation first = settlement.Committed().Deliver(settlement.First);
        FundsSimulation second = settlement.Committed().Deliver(settlement.Second);

        second.State(withLedger: false).ShouldBe(first.State(withLedger: false));
        first.Budget.Committed.ShouldBe(0m);
        first.Budget.Actual.ShouldBe(settlement.InvoicedTotal);
    }

    /// <summary>
    /// A property over histories that never refuse anything proves little, so this checks the generator reaches
    /// the cases the properties are about.
    /// </summary>
    [Fact]
    public void The_histories_reach_refusals_tombstones_retries_and_overspends()
    {
        FundsSimulation[] runs =
        [
            .. FundsGenerators.History.Sample(500)
                .Select(history => new FundsSimulation(history.Allotment).Deliver(history.WithRedeliveries())),
        ];

        runs.Count(run => run.Reservations.Values.Any(r => r.Refusal == FundsRefusal.InsufficientFunds)).ShouldBeGreaterThan(25);
        runs.Count(run => run.Orders.Values.Any(o => o.Refusal == FundsRefusal.InsufficientFunds)).ShouldBeGreaterThan(25);
        runs.Count(run => run.Reservations.Values.Any(r => r.Status == ReservationStatus.Released && r.BudgetId is null)).ShouldBeGreaterThan(25);
        runs.Count(run => run.Orders.Values.Any(o => o.Status == CommitmentStatus.Committed && o.LastAttempt > 1)).ShouldBeGreaterThan(25);
        runs.Count(run => run.Budget.Overspend > 0m).ShouldBeGreaterThan(25);
    }
}
