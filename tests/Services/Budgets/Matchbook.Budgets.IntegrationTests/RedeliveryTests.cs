using Matchbook.Budgets.Application.Features.Budgets;
using Matchbook.Budgets.Domain;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Requisitions;
using Matchbook.Testing;

namespace Matchbook.Budgets.IntegrationTests;

/// <summary>
/// A message delivered twice changes nothing: under the same message id the inbox drops it, and under a new id
/// (a publisher that sent it twice, or a redelivery after the inbox's window) the handler's own checks do.
/// </summary>
public sealed class RedeliveryTests(BudgetsFixture fixture) : IClassFixture<BudgetsFixture>
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task A_reservation_delivered_twice_under_one_message_id_is_reserved_and_answered_once()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(5_000m);
        RequisitionSubmitted submitted = BudgetsFixture.Submitted(budget, Guid.CreateVersion7(), 1_200m);
        Guid messageId = Guid.NewGuid();

        await fixture.Probe.PublishAsync(submitted, messageId);
        await fixture.Probe.PublishAsync(submitted, messageId);
        await fixture.Probe.WaitForAsync<FundsReserved>(r => r.RequisitionId == submitted.RequisitionId);
        await fixture.Probe.ShouldNotReceiveAsync<FundsReservationRejected>(r => r.RequisitionId == submitted.RequisitionId, Quiet);

        fixture.Probe.Received<FundsReserved>().Count(r => r.RequisitionId == submitted.RequisitionId).ShouldBe(1);
        (await fixture.BudgetAsync(budget.Id)).Reserved.ShouldBe(1_200m);
        await fixture.FiguresShouldEqualTheLedgerAsync(budget.Id);
    }

    [Fact]
    public async Task A_reservation_sent_again_under_a_new_message_id_changes_nothing_and_says_nothing()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(5_000m);
        RequisitionSubmitted submitted = BudgetsFixture.Submitted(budget, Guid.CreateVersion7(), 1_200m);
        await fixture.Probe.PublishAsync(submitted);
        await fixture.Probe.WaitForAsync<FundsReserved>(r => r.RequisitionId == submitted.RequisitionId);

        Guid again = Guid.NewGuid();
        await fixture.Probe.PublishAsync(submitted, again);
        await fixture.WaitUntilConsumedAsync(again);

        fixture.Probe.Received<FundsReserved>().Count(r => r.RequisitionId == submitted.RequisitionId).ShouldBe(1);
        fixture.Probe.Received<FundsReservationRejected>().ShouldNotContain(r => r.RequisitionId == submitted.RequisitionId);
        (await fixture.BudgetAsync(budget.Id)).Reserved.ShouldBe(1_200m);
        await fixture.FiguresShouldEqualTheLedgerAsync(budget.Id);
    }

    [Fact]
    public async Task A_refused_reservation_sent_again_after_funds_are_freed_is_still_refused()
    {
        // Requisitions ended the first one as BudgetRejected. Reserving for it now would hold money nobody will
        // ever release.
        BudgetView budget = await fixture.OpenBudgetAsync(1_000m);
        Guid holder = await fixture.ReserveAsync(budget, 1_000m);
        RequisitionSubmitted refused = BudgetsFixture.Submitted(budget, Guid.CreateVersion7(), 600m);
        await fixture.Probe.PublishAsync(refused);
        await fixture.Probe.WaitForAsync<FundsReservationRejected>(r => r.RequisitionId == refused.RequisitionId);
        await fixture.Probe.PublishAsync(new RequisitionCancelled(holder, TestUsers.Rita.Id, DateTimeOffset.UtcNow));
        await Eventually.MatchesAsync(() => fixture.BudgetAsync(budget.Id), static b => b.Reserved == 0m);

        Guid again = Guid.NewGuid();
        await fixture.Probe.PublishAsync(refused, again);
        await fixture.WaitUntilConsumedAsync(again);

        fixture.Probe.Received<FundsReserved>().ShouldNotContain(r => r.RequisitionId == refused.RequisitionId);
        (await fixture.BudgetAsync(budget.Id)).Reserved.ShouldBe(0m);
    }

    [Fact]
    public async Task A_commitment_requested_again_is_answered_committed_again_and_committed_once()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(5_000m);
        Guid requisition = await fixture.ReserveAsync(budget, 2_000m);
        Guid order = await fixture.CommitAsync(budget, requisition, 2_000m);

        await fixture.Probe.PublishAsync(BudgetsFixture.CommitmentRequested(budget, order, requisition, attempt: 1, 2_000m));
        await Eventually.MatchesAsync(
            () => Task.FromResult(fixture.Probe.Received<FundsCommitted>().Count(c => c.PurchaseOrderId == order)),
            static answers => answers == 2);

        BudgetView after = await fixture.BudgetAsync(budget.Id);
        (after.Reserved, after.Committed).ShouldBe((0m, 2_000m));
        (await fixture.LedgerAsync(budget.Id)).Count(e => e.Step == LedgerStep.Commit).ShouldBe(1);
    }

    [Fact]
    public async Task An_invoice_sent_again_under_a_new_message_id_is_spent_once()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(5_000m);
        Guid order = await fixture.SpendAsync(budget, 2_000m);
        InvoiceMatched invoice = BudgetsFixture.Invoiced(Guid.CreateVersion7(), order, 300m);
        await fixture.Probe.PublishAsync(invoice);

        Guid again = Guid.NewGuid();
        await fixture.Probe.PublishAsync(invoice, again);
        await fixture.WaitUntilConsumedAsync(again);
        await Eventually.MatchesAsync(() => fixture.BudgetAsync(budget.Id), static b => b.Actual == 2_300m);

        (await fixture.LedgerAsync(budget.Id)).Count(e => e.DocumentId == invoice.InvoiceId).ShouldBe(1);
        await fixture.FiguresShouldEqualTheLedgerAsync(budget.Id);
    }
}
