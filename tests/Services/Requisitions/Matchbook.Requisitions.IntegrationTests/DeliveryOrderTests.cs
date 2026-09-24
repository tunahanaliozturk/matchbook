using System.Net;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Requisitions;
using Matchbook.Contracts.Suppliers;
using Matchbook.Requisitions.Application.UseCases;
using Matchbook.Requisitions.Domain;
using Matchbook.Requisitions.Infrastructure;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>
/// Real messages over RabbitMQ in the orders a retry or a busy queue can produce, and again. Each message is
/// waited for until the consumer has finished with it, so "nothing changed" is an observation, not a guess.
/// Duplicates are sent under new message ids, which the inbox cannot recognise, so what keeps them harmless
/// is the requisition's own state.
/// </summary>
public sealed class DeliveryOrderTests(RequisitionsFixture fixture) : IClassFixture<RequisitionsFixture>
{
    [Fact]
    public async Task A_reservation_that_arrives_after_the_requester_cancelled_is_ignored()
    {
        RequisitionView submitted = await fixture.SubmittedAsync(TestUsers.Rita);
        await (await fixture.PostAsync(TestUsers.Rita, submitted.Id, "cancel")).ReadAsync<RequisitionView>();
        await fixture.Probe.WaitForAsync<RequisitionCancelled>(message => message.RequisitionId == submitted.Id);

        await fixture.DeliverAsync(Scenario.Reserved(submitted));

        RequisitionView after = await fixture.GetAsync(TestUsers.Rita, submitted.Id);
        after.Status.ShouldBe(RequisitionStatus.Cancelled);
        after.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_reservation_ends_the_requisition_and_a_late_reservation_does_not_revive_it()
    {
        RequisitionView submitted = await fixture.SubmittedAsync(TestUsers.Rita, 40_000m);

        await fixture.DeliverAsync(new FundsReservationRejected(
            submitted.Id, submitted.CostCentreCode, submitted.FiscalYear!.Value, 40_000m, 12_000m, FundsRejectionReason.InsufficientFunds, DateTimeOffset.UtcNow));
        await fixture.DeliverAsync(Scenario.Reserved(submitted));

        RequisitionView after = await fixture.GetAsync(TestUsers.Rita, submitted.Id);
        after.Status.ShouldBe(RequisitionStatus.BudgetRejected);
        after.RejectionReason.ShouldBe(FundsRejectionReason.InsufficientFunds);
        after.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_repeated_reservation_builds_no_second_route()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita, 150_000m);

        await fixture.DeliverAsync(Scenario.Reserved(pending));

        RequisitionView after = await fixture.GetAsync(TestUsers.Rita, pending.Id);
        after.Steps.Count.ShouldBe(3);
        after.Revision.ShouldBe(pending.Revision);
        after.Timeline.Count(static entry => entry.Action == TimelineAction.FundsReserved).ShouldBe(1);
    }

    [Fact]
    public async Task Cost_centre_snapshots_out_of_order_leave_the_newest()
    {
        string code = await fixture.CostCentreManagedByAsync(TestUsers.Mark.Id);

        await fixture.DeliverAsync(new CostCentreChanged(code, 3, "Version three", TestUsers.Maya.Id, true, DateTimeOffset.UtcNow));
        await fixture.DeliverAsync(new CostCentreChanged(code, 2, "Version two", TestUsers.Mark.Id, false, DateTimeOffset.UtcNow));

        CostCentre copy = await fixture.Host.InScopeAsync(services => services.GetRequiredService<RequisitionsDbContext>()
            .CostCentres.AsNoTracking().SingleAsync(centre => centre.Code == code));
        copy.Version.ShouldBe(3);
        copy.Name.ShouldBe("Version three");
        copy.ManagerId.ShouldBe(TestUsers.Maya.Id);
        copy.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task An_older_supplier_snapshot_cannot_reopen_a_blocked_supplier()
    {
        Guid supplierId = Guid.CreateVersion7();
        await fixture.DeliverAsync(new SupplierChanged(supplierId, 2, "Blocked BV", "NL", SupplierStatus.Blocked, 30, null, DateTimeOffset.UtcNow));
        await fixture.DeliverAsync(new SupplierChanged(supplierId, 1, "Blocked BV", "NL", SupplierStatus.Active, 30, null, DateTimeOffset.UtcNow));

        RequisitionView draft = await (await fixture.PostCreateAsync(TestUsers.Rita, Scenario.Request(2_000m) with { SupplierId = supplierId }))
            .ReadAsync<RequisitionView>(HttpStatusCode.Created);

        await (await fixture.PostAsync(TestUsers.Rita, draft.Id, "submit"))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, RequisitionCodes.SupplierInactive);
    }

    [Fact]
    public async Task A_supplier_status_this_service_does_not_know_counts_as_not_active()
    {
        Guid supplierId = Guid.CreateVersion7();
        await fixture.DeliverAsync(new SupplierChanged(supplierId, 1, "Unknown BV", "NL", "UnderReview", 30, null, DateTimeOffset.UtcNow));

        RequisitionView draft = await (await fixture.PostCreateAsync(TestUsers.Rita, Scenario.Request(2_000m) with { SupplierId = supplierId }))
            .ReadAsync<RequisitionView>(HttpStatusCode.Created);

        await (await fixture.PostAsync(TestUsers.Rita, draft.Id, "submit"))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, RequisitionCodes.SupplierInactive);
    }

    [Fact]
    public async Task An_issued_order_makes_it_ordered_a_closed_one_closed_and_repeats_change_nothing()
    {
        RequisitionView approved = await ApprovedAsync();
        Guid orderId = Guid.CreateVersion7();
        PurchaseOrderIssued issued = Issued(approved, orderId);

        await fixture.DeliverAsync(issued);
        RequisitionView ordered = await fixture.GetAsync(TestUsers.Rita, approved.Id);
        ordered.Status.ShouldBe(RequisitionStatus.Ordered);
        ordered.PurchaseOrderNumber.ShouldBe("PO-2026-000017");

        await fixture.DeliverAsync(issued);
        (await fixture.GetAsync(TestUsers.Rita, approved.Id)).Revision.ShouldBe(ordered.Revision);

        PurchaseOrderClosed closed = new(orderId, approved.Id, PurchaseOrderCloseReason.Completed, DateTimeOffset.UtcNow);
        await fixture.DeliverAsync(closed);
        RequisitionView done = await fixture.GetAsync(TestUsers.Rita, approved.Id);
        done.Status.ShouldBe(RequisitionStatus.Closed);

        await fixture.DeliverAsync(closed);
        await fixture.DeliverAsync(issued);
        RequisitionView after = await fixture.GetAsync(TestUsers.Rita, approved.Id);
        after.Status.ShouldBe(RequisitionStatus.Closed);
        after.Revision.ShouldBe(done.Revision);
    }

    [Fact]
    public async Task A_close_that_overtakes_the_issue_still_leaves_the_order_number()
    {
        RequisitionView approved = await ApprovedAsync();
        Guid orderId = Guid.CreateVersion7();

        await fixture.DeliverAsync(new PurchaseOrderClosed(orderId, approved.Id, PurchaseOrderCloseReason.ShortClosed, DateTimeOffset.UtcNow));
        await fixture.DeliverAsync(Issued(approved, orderId));

        RequisitionView after = await fixture.GetAsync(TestUsers.Rita, approved.Id);
        after.Status.ShouldBe(RequisitionStatus.Closed);
        after.PurchaseOrderId.ShouldBe(orderId);
        after.PurchaseOrderNumber.ShouldBe("PO-2026-000017");
    }

    [Fact]
    public async Task An_issue_replayed_before_approval_cannot_skip_the_approvers()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita);

        await fixture.DeliverAsync(Issued(pending, Guid.CreateVersion7()));

        RequisitionView after = await fixture.GetAsync(TestUsers.Rita, pending.Id);
        after.Status.ShouldBe(RequisitionStatus.PendingApproval);
        after.PurchaseOrderNumber.ShouldBeNull();
    }

    private async Task<RequisitionView> ApprovedAsync()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita);
        return await (await fixture.PostAsync(TestUsers.Mark, pending.Id, "approve")).ReadAsync<RequisitionView>();
    }

    private static PurchaseOrderIssued Issued(RequisitionView requisition, Guid orderId) =>
        new(
            orderId,
            "PO-2026-000017",
            requisition.Id,
            requisition.SupplierId,
            requisition.CostCentreCode,
            requisition.FiscalYear!.Value,
            [.. requisition.Lines.Select(static line => new PurchaseOrderLine(
                line.LineNumber, line.Description, line.Quantity, line.UnitOfMeasure, line.UnitPrice, line.Amount))],
            requisition.Amount,
            TestUsers.Bruno.Id,
            DateTimeOffset.UtcNow);
}
