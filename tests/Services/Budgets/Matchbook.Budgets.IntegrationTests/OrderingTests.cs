using Matchbook.Budgets.Application.Budgets;
using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Requisitions;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.IntegrationTests;

/// <summary>
/// Events that arrive in the "wrong" order. Each test waits for one message to be handled before sending the
/// next, so the order is the one the test names rather than whatever the broker picked.
/// </summary>
public sealed class OrderingTests(BudgetsFixture fixture) : IClassFixture<BudgetsFixture>
{
    [Fact]
    public async Task A_cancellation_that_arrives_first_leaves_a_tombstone_and_the_late_reservation_is_refused()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(5_000m);
        Guid requisition = Guid.CreateVersion7();

        await fixture.Probe.PublishAsync(new RequisitionCancelled(requisition, TestUsers.Rita.Id, DateTimeOffset.UtcNow));
        await Eventually.MatchesAsync(
            () => fixture.InDatabaseAsync(db => db.Reservations.AnyAsync(r => r.RequisitionId == requisition)),
            static tombstoned => tombstoned);
        await fixture.Probe.PublishAsync(BudgetsFixture.Submitted(budget, requisition, 1_000m));

        FundsReservationRejected refused =
            await fixture.Probe.WaitForAsync<FundsReservationRejected>(r => r.RequisitionId == requisition);
        refused.Reason.ShouldBe(FundsRejectionReason.DocumentClosed);
        fixture.Probe.Received<FundsReserved>().ShouldNotContain(r => r.RequisitionId == requisition);
        (await fixture.BudgetAsync(budget.Id)).Reserved.ShouldBe(0m);
    }

    [Fact]
    public async Task An_order_closed_before_its_commitment_request_releases_the_reservation_once_and_refuses_the_request()
    {
        // What Purchasing relies on: a cancelled draft's late or retried commitment request is answered, with
        // document_closed, and does not commit anything.
        BudgetView budget = await fixture.OpenBudgetAsync(5_000m);
        Guid requisition = await fixture.ReserveAsync(budget, 1_500m);
        Guid order = Guid.CreateVersion7();
        var closed = new PurchaseOrderClosed(order, requisition, PurchaseOrderCloseReason.Cancelled, DateTimeOffset.UtcNow);

        await fixture.Probe.PublishAsync(closed);
        await Eventually.MatchesAsync(() => fixture.BudgetAsync(budget.Id), static b => b.Reserved == 0m);
        await fixture.Probe.PublishAsync(BudgetsFixture.CommitmentRequested(budget, order, requisition, attempt: 1, 1_500m));

        FundsCommitmentRejected rejected =
            await fixture.Probe.WaitForAsync<FundsCommitmentRejected>(r => r.PurchaseOrderId == order);
        (rejected.Reason, rejected.Attempt).ShouldBe((FundsRejectionReason.DocumentClosed, 1));
        fixture.Probe.Received<FundsCommitted>().ShouldNotContain(c => c.PurchaseOrderId == order);

        Guid again = Guid.NewGuid();
        await fixture.Probe.PublishAsync(closed, again);
        await fixture.WaitUntilConsumedAsync(again);

        LedgerEntry[] ledger = await fixture.LedgerAsync(budget.Id);
        ledger.Count(e => e.DocumentId == requisition && e.Step == LedgerStep.Release).ShouldBe(1);
        ledger.ShouldNotContain(e => e.DocumentId == order);
        BudgetView after = await fixture.BudgetAsync(budget.Id);
        (after.Reserved, after.Committed, after.Available).ShouldBe((0m, 0m, 5_000m));
    }

    [Fact]
    public async Task A_stale_commitment_attempt_is_ignored_and_only_a_newer_one_is_decided()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(2_000m);
        Guid requisition = await fixture.ReserveAsync(budget, 1_000m);
        Guid order = Guid.CreateVersion7();

        // Attempt 2 arrives first and is refused: 5,000 is more than the 1,000 available plus the 1,000 reserved.
        await fixture.Probe.PublishAsync(BudgetsFixture.CommitmentRequested(budget, order, requisition, attempt: 2, 5_000m));
        await fixture.Probe.WaitForAsync<FundsCommitmentRejected>(r => r.PurchaseOrderId == order && r.Attempt == 2);

        // Attempt 1 would fit, but Purchasing has moved past it. Granting it would commit an order that is back
        // in draft, at a price the buyer may already have changed.
        Guid stale = Guid.NewGuid();
        await fixture.Probe.PublishAsync(
            BudgetsFixture.CommitmentRequested(budget, order, requisition, attempt: 1, 1_000m), stale);
        await fixture.WaitUntilConsumedAsync(stale);
        fixture.Probe.Received<FundsCommitted>().ShouldNotContain(c => c.PurchaseOrderId == order);
        fixture.Probe.Received<FundsCommitmentRejected>().ShouldNotContain(r => r.PurchaseOrderId == order && r.Attempt == 1);
        (await fixture.BudgetAsync(budget.Id)).Committed.ShouldBe(0m);

        await fixture.Probe.PublishAsync(BudgetsFixture.CommitmentRequested(budget, order, requisition, attempt: 3, 1_500m));
        FundsCommitted committed = await fixture.Probe.WaitForAsync<FundsCommitted>(c => c.PurchaseOrderId == order);

        (committed.Attempt, committed.Amount).ShouldBe((3, 1_500m));
        BudgetView after = await fixture.BudgetAsync(budget.Id);
        (after.Reserved, after.Committed, after.Available).ShouldBe((0m, 1_500m, 500m));
    }

    [Fact]
    public async Task Invoices_then_close_and_close_then_invoices_end_in_identical_figures()
    {
        // Two identical budgets and orders of 5,000, invoiced 3,000 and 2,600: 600 beyond the commitment.
        (BudgetView budget, Guid order)[] cases = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            BudgetView budget = await fixture.OpenBudgetAsync(10_000m);
            Guid order = await fixture.CommitAsync(budget, await fixture.ReserveAsync(budget, 5_000m), 5_000m);
            return (budget, order);
        }));

        (BudgetView invoicedFirst, Guid first) = cases[0];
        await InvoiceAsync(first, 3_000m);
        await InvoiceAsync(first, 2_600m);
        await CloseAsync(first);

        (BudgetView closedFirst, Guid second) = cases[1];
        await CloseAsync(second);
        await InvoiceAsync(second, 3_000m);
        await InvoiceAsync(second, 2_600m);

        BudgetView one = await fixture.BudgetAsync(invoicedFirst.Id);
        BudgetView two = await fixture.BudgetAsync(closedFirst.Id);
        (one.Reserved, one.Committed, one.Actual, one.Available).ShouldBe((0m, 0m, 5_600m, 4_400m));
        (two.Reserved, two.Committed, two.Actual, two.Available).ShouldBe((one.Reserved, one.Committed, one.Actual, one.Available));
        await fixture.FiguresShouldEqualTheLedgerAsync(invoicedFirst.Id);
        await fixture.FiguresShouldEqualTheLedgerAsync(closedFirst.Id);
    }

    [Fact]
    public async Task A_commitment_on_an_order_whose_requisition_was_never_seen_takes_the_whole_amount_from_available()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(1_000m);
        Guid order = Guid.CreateVersion7();

        await fixture.Probe.PublishAsync(BudgetsFixture.CommitmentRequested(budget, order, Guid.CreateVersion7(), attempt: 1, 1_000.01m));
        FundsCommitmentRejected rejected = await fixture.Probe.WaitForAsync<FundsCommitmentRejected>(r => r.PurchaseOrderId == order);

        (rejected.Reason, rejected.Available).ShouldBe((FundsRejectionReason.InsufficientFunds, 1_000m));
    }

    private async Task InvoiceAsync(Guid order, decimal amount)
    {
        Guid invoice = Guid.CreateVersion7();
        await fixture.Probe.PublishAsync(BudgetsFixture.Invoiced(invoice, order, amount));
        await Eventually.MatchesAsync(
            () => fixture.InDatabaseAsync(db => db.Ledger.AnyAsync(e => e.DocumentId == invoice)),
            static recorded => recorded);
    }

    private async Task CloseAsync(Guid order)
    {
        await fixture.Probe.PublishAsync(
            new PurchaseOrderClosed(order, Guid.CreateVersion7(), PurchaseOrderCloseReason.ShortClosed, DateTimeOffset.UtcNow));
        await Eventually.MatchesAsync(
            () => fixture.InDatabaseAsync(db => db.Commitments.AnyAsync(o => o.PurchaseOrderId == order && o.Status == CommitmentStatus.Closed)),
            static closed => closed);
    }
}
