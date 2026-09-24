using System.Net;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Suppliers;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.Purchasing.Infrastructure.Persistence;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Purchasing.IntegrationTests;

public sealed class IssuingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    private readonly EventProbe _probe = fixture.Probe;
    private readonly HttpClient _bruno = fixture.ClientFor(TestUsers.Bruno);

    [Fact]
    public async Task An_approved_requisition_becomes_a_draft_that_bruno_amends_and_issues_once_budgets_commits()
    {
        Guid supplierId = await fixture.ActiveSupplierAsync();

        PurchaseOrderView draft = await fixture.DraftAsync(supplierId, (10m, 12.50m), (4m, 99.99m));

        draft.Status.ShouldBe(PurchaseOrderStatus.Draft);
        draft.Number.ShouldMatch(@"^PO-\d{4}-\d{6,}$");
        draft.Amount.ShouldBe(524.96m);

        PurchaseOrderView amended = await (await _bruno.PutJsonAsync(Routes.Line(draft.Id, 2), new { quantity = 2m, unitPrice = 99.99m }))
            .ReadAsync<PurchaseOrderView>(HttpStatusCode.OK);
        amended.Amount.ShouldBe(324.98m);

        PurchaseOrderCommitmentRequested request = await fixture.IssueAsync(draft.Id, attempt: 1);
        request.Amount.ShouldBe(324.98m);
        request.RequisitionId.ShouldBe(draft.RequisitionId);
        request.CostCentreCode.ShouldBe("ENG-PLATFORM");

        await _probe.PublishAsync(Messages.Committed(request));

        PurchaseOrderIssued issued = await _probe.WaitForAsync<PurchaseOrderIssued>(message => message.PurchaseOrderId == draft.Id);
        issued.IssuedBy.ShouldBe(TestUsers.Bruno.Id);
        issued.Number.ShouldBe(draft.Number);
        issued.SupplierId.ShouldBe(supplierId);
        issued.Amount.ShouldBe(324.98m);
        issued.Lines.Select(static line => (line.LineNumber, line.Quantity, line.Amount)).ShouldBe([(1, 10m, 125.00m), (2, 2m, 199.98m)]);
        (await fixture.GetAsync(draft.Id)).Status.ShouldBe(PurchaseOrderStatus.Issued);
    }

    [Fact]
    public async Task A_rejection_returns_the_order_to_draft_the_reissue_is_attempt_two_and_a_late_reply_to_attempt_one_is_ignored()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (3m, 100m));
        PurchaseOrderCommitmentRequested first = await fixture.IssueAsync(draft.Id, attempt: 1);

        await _probe.PublishAsync(Messages.Rejected(first, FundsRejectionReason.InsufficientFunds));
        PurchaseOrderView rejected = await Eventually.MatchesAsync(
            () => fixture.GetAsync(draft.Id), static order => order.Status == PurchaseOrderStatus.Draft);
        rejected.CommitmentRejectionReason.ShouldBe(FundsRejectionReason.InsufficientFunds);

        PurchaseOrderCommitmentRequested second = await fixture.IssueAsync(draft.Id, attempt: 2);

        await fixture.PublishAndWaitAsync(Messages.Committed(first));
        PurchaseOrderView stillWaiting = await fixture.GetAsync(draft.Id);
        stillWaiting.Status.ShouldBe(PurchaseOrderStatus.CommitmentPending);
        stillWaiting.CommitmentAttempt.ShouldBe(2);

        await _probe.PublishAsync(Messages.Committed(second));
        await _probe.WaitForAsync<PurchaseOrderIssued>(issued => issued.PurchaseOrderId == draft.Id);
        (await fixture.CountAfterSettlingAsync<PurchaseOrderIssued>(issued => issued.PurchaseOrderId == draft.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Redelivered_replies_change_nothing()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (5m, 20m));
        PurchaseOrderCommitmentRequested request = await fixture.IssueAsync(draft.Id, attempt: 1);
        FundsCommitted committed = Messages.Committed(request);
        Guid messageId = Guid.NewGuid();

        await fixture.PublishAndWaitAsync(committed, messageId);
        PurchaseOrderView issued = await fixture.GetAsync(draft.Id);

        // The same message again (the inbox drops it), the same reply under a new message id (past the inbox, so
        // the attempt rule has to hold), and a stray rejection of the attempt that already succeeded.
        await _probe.PublishAsync(committed, messageId);
        await fixture.PublishAndWaitAsync(committed);
        await fixture.PublishAndWaitAsync(Messages.Rejected(request, FundsRejectionReason.NoBudget));

        PurchaseOrderView after = await fixture.GetAsync(draft.Id);
        after.Status.ShouldBe(PurchaseOrderStatus.Issued);
        after.IssuedAt.ShouldBe(issued.IssuedAt);
        after.CommitmentRejectionReason.ShouldBeNull();
        (await fixture.CountAfterSettlingAsync<PurchaseOrderIssued>(message => message.PurchaseOrderId == draft.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_while_budgets_is_deciding_ends_cancelled_even_when_the_funds_are_committed_afterwards()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (2m, 250m));
        PurchaseOrderCommitmentRequested request = await fixture.IssueAsync(draft.Id, attempt: 1);

        PurchaseOrderView cancelled = await (await _bruno.PostAsync(Routes.Cancel(draft.Id), null)).ReadAsync<PurchaseOrderView>(HttpStatusCode.OK);
        cancelled.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        PurchaseOrderClosed closed = await _probe.WaitForAsync<PurchaseOrderClosed>(message => message.PurchaseOrderId == draft.Id);
        closed.Reason.ShouldBe(PurchaseOrderCloseReason.Cancelled);
        closed.RequisitionId.ShouldBe(draft.RequisitionId);

        await fixture.PublishAndWaitAsync(Messages.Committed(request));

        (await fixture.GetAsync(draft.Id)).Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        (await fixture.CountAfterSettlingAsync<PurchaseOrderClosed>(message => message.PurchaseOrderId == draft.Id)).ShouldBe(1);
        _probe.Received<PurchaseOrderIssued>().ShouldNotContain(message => message.PurchaseOrderId == draft.Id);
    }

    [Fact]
    public async Task An_order_cannot_be_issued_to_a_supplier_purchasing_has_never_heard_of()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(Guid.CreateVersion7(), (1m, 10m));

        await (await _bruno.PostAsync(Routes.Issue(draft.Id), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.supplier_not_active");
    }

    [Fact]
    public async Task Supplier_snapshots_out_of_order_keep_the_newest_and_a_blocked_supplier_cannot_be_ordered_from()
    {
        Guid supplierId = Guid.CreateVersion7();
        await fixture.PublishAndWaitAsync(Messages.Supplier(supplierId, 3, SupplierStatus.Blocked));
        await fixture.PublishAndWaitAsync(Messages.Supplier(supplierId, 2, SupplierStatus.Active));

        Supplier copy = await fixture.Host.InScopeAsync(services => services.GetRequiredService<PurchasingDbContext>()
            .Suppliers.AsNoTracking().SingleAsync(supplier => supplier.Id == supplierId));
        copy.Version.ShouldBe(3);
        copy.IsActive.ShouldBeFalse();

        PurchaseOrderView draft = await fixture.DraftAsync(supplierId, (1m, 10m));
        await (await _bruno.PostAsync(Routes.Issue(draft.Id), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.supplier_not_active");

        await fixture.PublishAndWaitAsync(Messages.Supplier(supplierId, 4, SupplierStatus.Active));
        await fixture.IssueAsync(draft.Id, attempt: 1);
    }

    [Fact]
    public async Task An_approval_delivered_twice_drafts_one_order()
    {
        var approval = Messages.Approval(await fixture.ActiveSupplierAsync(), (1m, 1m));

        // Two message ids, so both copies get past the inbox and the unique requisition id has to hold.
        await fixture.PublishAndWaitAsync(approval);
        await fixture.PublishAndWaitAsync(approval);

        int orders = await fixture.Host.InScopeAsync(services => services.GetRequiredService<PurchasingDbContext>()
            .PurchaseOrders.CountAsync(order => order.RequisitionId == approval.RequisitionId));
        orders.ShouldBe(1);
    }
}
