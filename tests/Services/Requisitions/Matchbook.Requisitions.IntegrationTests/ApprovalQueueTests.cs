using System.Net;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Application.Features.Requisitions;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary><c>GET /approvals</c>: what each approver can act on now, and nothing they cannot.</summary>
public sealed class ApprovalQueueTests(RequisitionsFixture fixture) : IClassFixture<RequisitionsFixture>
{
    [Fact]
    public async Task Each_approver_sees_exactly_the_requisitions_waiting_on_them_now()
    {
        Actor frank = TestUsers.Stranger(Roles.FinanceApprover);

        RequisitionView markToSign = await fixture.PendingApprovalAsync(TestUsers.Rita, 5_000m);
        RequisitionView financeToSign = await fixture.PendingApprovalAsync(TestUsers.Rita, 50_000m);
        await (await fixture.PostAsync(TestUsers.Mark, financeToSign.Id, "approve")).ReadAsync<RequisitionView>();
        RequisitionView mayaToSign = await fixture.PendingApprovalAsync(TestUsers.Rita, 250_000m, RequisitionsFixture.Growth);
        RequisitionView carlToSign = await fixture.PendingApprovalAsync(TestUsers.Rita, 250_000m);
        await (await fixture.PostAsync(TestUsers.Mark, carlToSign.Id, "approve")).ReadAsync<RequisitionView>();
        await (await fixture.PostAsync(TestUsers.Fiona, carlToSign.Id, "approve")).ReadAsync<RequisitionView>();

        // Nobody can act on these: not yet reserved, still a draft, or withdrawn.
        RequisitionView awaitingBudgets = await fixture.SubmittedAsync(TestUsers.Rita, 5_000m);
        RequisitionView draft = await fixture.CreateAsync(TestUsers.Rita, 5_000m);
        RequisitionView cancelled = await fixture.PendingApprovalAsync(TestUsers.Rita, 5_000m);
        await (await fixture.PostAsync(TestUsers.Rita, cancelled.Id, "cancel")).ReadAsync<RequisitionView>();

        Guid[] mine = [markToSign.Id, financeToSign.Id, mayaToSign.Id, carlToSign.Id, awaitingBudgets.Id, draft.Id, cancelled.Id];

        (await QueueAsync(TestUsers.Mark)).ShouldBe([markToSign.Id]);
        (await QueueAsync(TestUsers.Maya)).ShouldBe([mayaToSign.Id]);
        (await QueueAsync(TestUsers.Fiona)).ShouldBe([financeToSign.Id]);
        (await QueueAsync(frank)).ShouldBe([financeToSign.Id]);
        (await QueueAsync(TestUsers.Carl)).ShouldBe([carlToSign.Id]);

        // Deciding takes it off every queue it was on.
        await (await fixture.PostAsync(frank, financeToSign.Id, "approve")).ReadAsync<RequisitionView>();
        (await QueueAsync(TestUsers.Fiona)).ShouldBeEmpty();
        (await QueueAsync(frank)).ShouldBeEmpty();

        async Task<Guid[]> QueueAsync(Actor approver)
        {
            Page<RequisitionSummary> page = await fixture.ApprovalsAsync(approver, "?limit=200");
            return [.. page.Items.Select(static item => item.Id).Where(mine.Contains)];
        }
    }

    [Fact]
    public async Task The_queue_pages_from_the_longest_waiting()
    {
        Actor manager = TestUsers.Stranger(Roles.Approver);
        string costCentre = await fixture.CostCentreManagedByAsync(manager.Id);
        RequisitionView first = await fixture.PendingApprovalAsync(TestUsers.Rita, 1_000m, costCentre);
        RequisitionView second = await fixture.PendingApprovalAsync(TestUsers.Rita, 1_000m, costCentre);
        RequisitionView third = await fixture.PendingApprovalAsync(TestUsers.Rita, 1_000m, costCentre);

        Page<RequisitionSummary> page = await fixture.ApprovalsAsync(manager, "?limit=2");
        Page<RequisitionSummary> next = await fixture.ApprovalsAsync(manager, $"?limit=2&after={page.NextCursor}");

        page.Items.Select(static item => item.Id).ShouldBe([first.Id, second.Id]);
        next.Items.Select(static item => item.Id).ShouldBe([third.Id]);
        next.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Only_approvers_have_a_queue()
    {
        (await fixture.Client(TestUsers.Rita).GetAsync(Scenario.Url("/approvals"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Client(TestUsers.Audrey).GetAsync(Scenario.Url("/approvals"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
