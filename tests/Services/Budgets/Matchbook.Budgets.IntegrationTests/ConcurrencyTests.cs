using System.Diagnostics;
using Matchbook.Budgets.Application.Features.Budgets;
using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Requisitions;
using Matchbook.Testing;

namespace Matchbook.Budgets.IntegrationTests;

/// <summary>
/// The headline guarantee: nothing is ever reserved or committed beyond available, however many requests race
/// for the same budget. Every message goes through the real broker, the real consumers (sixteen at a time per
/// queue), the outbox and Postgres.
/// </summary>
public sealed class ConcurrencyTests(BudgetsFixture fixture) : IClassFixture<BudgetsFixture>
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Two_hundred_simultaneous_reservations_against_room_for_fifty_grant_exactly_fifty()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(50_000m);
        RequisitionSubmitted[] submissions =
            [.. Enumerable.Range(0, 200).Select(_ => BudgetsFixture.Submitted(budget, Guid.CreateVersion7(), 1_000m))];
        HashSet<Guid> requisitions = [.. submissions.Select(s => s.RequisitionId)];

        var clock = Stopwatch.StartNew();
        await Task.WhenAll(submissions.Select(fixture.Probe.PublishAsync));
        await Eventually.MatchesAsync(
            () => Task.FromResult(
                fixture.Probe.Received<FundsReserved>().Count(r => requisitions.Contains(r.RequisitionId))
                + fixture.Probe.Received<FundsReservationRejected>().Count(r => requisitions.Contains(r.RequisitionId))),
            static answered => answered == 200,
            Deadline);
        clock.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"200 reservations published and answered in {clock.Elapsed.TotalMilliseconds:0} ms");

        fixture.Probe.Received<FundsReserved>().Count(r => requisitions.Contains(r.RequisitionId)).ShouldBe(50);
        FundsReservationRejected[] refused =
            [.. fixture.Probe.Received<FundsReservationRejected>().Where(r => requisitions.Contains(r.RequisitionId))];
        refused.Length.ShouldBe(150);
        refused.ShouldAllBe(r => r.Reason == FundsRejectionReason.InsufficientFunds);

        BudgetView after = await fixture.BudgetAsync(budget.Id);
        after.Reserved.ShouldBe(50_000m);
        after.Available.ShouldBe(0m);
        (await fixture.LedgerAsync(budget.Id)).Count(e => e.Step == LedgerStep.Reserve).ShouldBe(50);
        await fixture.FiguresShouldEqualTheLedgerAsync(budget.Id);
    }

    [Fact]
    public async Task Fifty_simultaneous_commitments_racing_for_the_last_of_a_budget_commit_exactly_what_fits()
    {
        // 10,000 is already spent, so the check constraint (reserved + committed <= allotted) would let 35 orders
        // through: only the conditional UPDATE, which counts actual, holds the line here. Fifty requisitions of 500
        // then hold 25,000 of the 50,000 left. Each order is for 1,500, so committing it takes its own 500 back and
        // needs 1,000 more: room for exactly twenty-five.
        BudgetView budget = await fixture.OpenBudgetAsync(60_000m);
        await fixture.SpendAsync(budget, 10_000m);
        Guid[] requisitions = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => fixture.ReserveAsync(budget, 500m)));
        var orders = requisitions.ToDictionary(_ => Guid.CreateVersion7());

        var clock = Stopwatch.StartNew();
        await Task.WhenAll(orders.Select(order =>
            fixture.Probe.PublishAsync(BudgetsFixture.CommitmentRequested(budget, order.Key, order.Value, attempt: 1, 1_500m))));
        await Eventually.MatchesAsync(
            () => Task.FromResult(
                fixture.Probe.Received<FundsCommitted>().Count(c => orders.ContainsKey(c.PurchaseOrderId))
                + fixture.Probe.Received<FundsCommitmentRejected>().Count(c => orders.ContainsKey(c.PurchaseOrderId))),
            static answered => answered == 50,
            Deadline);
        clock.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"50 commitments published and answered in {clock.Elapsed.TotalMilliseconds:0} ms");

        fixture.Probe.Received<FundsCommitted>().Count(c => orders.ContainsKey(c.PurchaseOrderId)).ShouldBe(25);
        FundsCommitmentRejected[] refused =
            [.. fixture.Probe.Received<FundsCommitmentRejected>().Where(c => orders.ContainsKey(c.PurchaseOrderId))];
        refused.Length.ShouldBe(25);
        refused.ShouldAllBe(r => r.Reason == FundsRejectionReason.InsufficientFunds && r.Attempt == 1);

        // A refused order's reservation stands, as the contract promises.
        BudgetView after = await fixture.BudgetAsync(budget.Id);
        after.Committed.ShouldBe(25 * 1_500m);
        after.Reserved.ShouldBe(25 * 500m);
        after.Actual.ShouldBe(10_000m);
        after.Available.ShouldBe(0m);
        await fixture.FiguresShouldEqualTheLedgerAsync(budget.Id);
    }
}
