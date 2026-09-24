using System.Net;
using System.Net.Http.Json;
using Matchbook.Contracts.Payables;
using Matchbook.Payables.Api.Invoices;
using Matchbook.Payables.Application;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>The three-way match through the real host: events in over RabbitMQ, invoices in over HTTP.</summary>
public sealed class MatchingTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>
{
    private readonly Scenario _given = new(fixture);

    [Fact]
    public async Task An_invoice_that_matches_its_order_and_receipt_is_published_with_its_lines_and_amount()
    {
        Guid supplier = await _given.SupplierAsync(paymentTermsDays: 30);
        Guid order = await _given.OrderAsync(supplier, (1, 10, 12.5m), (2, 4, 99.99m));
        await _given.ReceiveAsync(order, (1, 10), (2, 4));
        DateOnly invoiceDate = Scenario.Today.AddDays(-2);

        InvoiceView invoice = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, order, "INV-2026-0001", invoiceDate, (1, 10, 12.5m), (2, 4, 99.99m)));

        invoice.Status.ShouldBe(InvoiceStatus.Payable);
        invoice.Reason.ShouldBe(MatchReason.None);
        invoice.DueDate.ShouldBe(invoiceDate.AddDays(30));
        InvoiceMatched matched = await _given.Probe.WaitForAsync<InvoiceMatched>(message => message.InvoiceId == invoice.Id);
        matched.PurchaseOrderId.ShouldBe(order);
        matched.SupplierId.ShouldBe(supplier);
        matched.SupplierInvoiceNumber.ShouldBe("INV-2026-0001");
        matched.Amount.ShouldBe(524.96m);
        matched.Lines.ShouldBe([new MatchedLine(1, 10, 12.5m, 125.00m), new MatchedLine(2, 4, 99.99m, 399.96m)]);
    }

    [Fact]
    public async Task An_invoice_captured_before_its_goods_waits_and_matches_when_they_arrive()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        await _given.ReceiveAsync(order, (1, 4));

        InvoiceView invoice = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, order, "INV-LATE-GOODS", Scenario.Today, (1, 10, 10m)));
        invoice.Status.ShouldBe(InvoiceStatus.AwaitingReceipt);
        invoice.ReasonDetail.ShouldBe("Line 1: 10 invoiced in total against 4 received.");

        await _given.Probe.PublishAsync(Scenario.Receipt(order, (1, 6)));

        await _given.InvoiceInAsync(invoice.Id, InvoiceStatus.Payable);
        await _given.Probe.WaitForAsync<InvoiceMatched>(message => message.InvoiceId == invoice.Id);
    }

    [Fact]
    public async Task An_invoice_captured_before_its_order_waits_and_matches_when_the_order_arrives()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = Guid.CreateVersion7();

        InvoiceView invoice = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, order, "INV-EARLY", Scenario.Today, (1, 3, 20m)));
        invoice.Status.ShouldBe(InvoiceStatus.AwaitingPurchaseOrder);

        // The receipt arrives before the order too, and is kept for it.
        await _given.ReceiveAsync(order, (1, 3));
        (await _given.InvoiceAsync(invoice.Id)).Status.ShouldBe(InvoiceStatus.AwaitingPurchaseOrder);

        await _given.Probe.PublishAsync(Scenario.OrderIssued(order, supplier, (1, 3, 20m)));

        await _given.InvoiceInAsync(invoice.Id, InvoiceStatus.Payable);
        (await _given.Probe.WaitForAsync<InvoiceMatched>(message => message.InvoiceId == invoice.Id)).Amount.ShouldBe(60m);
    }

    [Fact]
    public async Task A_price_exactly_at_tolerance_matches_and_a_ten_thousandth_more_is_a_variance()
    {
        // 10 x 10.00 is 100.00, so 2.00 of variance is allowed: 0.20 a unit. 10 x 20.00 allows 0.40, and not 0.4001.
        // Different totals, or the second would be held as a suspected duplicate of the first.
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m), (2, 10, 20m));
        await _given.ReceiveAsync(order, (1, 10), (2, 10));

        InvoiceView inside = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, order, "INV-EDGE-IN", Scenario.Today, (1, 10, 10.20m)));
        InvoiceView outside = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, order, "INV-EDGE-OUT", Scenario.Today, (2, 10, 20.4001m)));

        inside.Status.ShouldBe(InvoiceStatus.Payable);
        outside.Status.ShouldBe(InvoiceStatus.PriceVariance);
        outside.Reason.ShouldBe(MatchReason.PriceVarianceBeyondTolerance);
    }

    [Fact]
    public async Task An_approver_accepts_a_variance_but_neither_a_clerk_nor_the_approver_who_captured_it_can()
    {
        Actor both = TestUsers.Stranger(Roles.ApClerk, Roles.ApApprover);
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m), (2, 10, 10m));
        await _given.ReceiveAsync(order, (1, 10), (2, 10));
        InvoiceView byClerk = await _given.CaptureAsync(TestUsers.Alice, Scenario.Invoice(supplier, order, "INV-VAR-1", Scenario.Today, (1, 10, 11m)));
        InvoiceView byApprover = await _given.CaptureAsync(both, Scenario.Invoice(supplier, order, "INV-VAR-2", Scenario.Today, (2, 10, 11m)));

        await (await AcceptAsync(TestUsers.Alice, byClerk.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "invoice.approver_required");
        await (await AcceptAsync(both, byApprover.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "invoice.self_approval");

        InvoiceView accepted = await Scenario.ReadAsync<InvoiceView>(await AcceptAsync(TestUsers.Aaron, byClerk.Id), HttpStatusCode.OK);

        accepted.Status.ShouldBe(InvoiceStatus.Payable);
        accepted.Reason.ShouldBe(MatchReason.PriceVarianceAccepted);
        accepted.VarianceAcceptedBy.ShouldBe(TestUsers.Aaron.Id);
        accepted.VarianceAcceptanceReason.ShouldBe("Freight surcharge agreed with the buyer.");
        (await _given.Probe.WaitForAsync<InvoiceMatched>(message => message.InvoiceId == byClerk.Id)).Amount.ShouldBe(110m);
    }

    [Fact]
    public async Task A_suspected_duplicate_is_held_until_an_approver_who_did_not_capture_it_clears_it()
    {
        Actor both = TestUsers.Stranger(Roles.ApClerk, Roles.ApApprover);
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        await _given.ReceiveAsync(order, (1, 10));
        InvoiceView first = await _given.CaptureAsync(both, Scenario.Invoice(supplier, order, "INV-5001", Scenario.Today.AddDays(-5), (1, 5, 10m)));

        InvoiceView second = await _given.CaptureAsync(both, Scenario.Invoice(supplier, order, "INV-5002", Scenario.Today, (1, 5, 10m)));

        second.Status.ShouldBe(InvoiceStatus.SuspectedDuplicate);
        second.SuspectedDuplicateOf.ShouldBe(first.Id);
        Page<InvoiceSummary> queue = (await _given.As(TestUsers.Aaron).GetFromJsonAsync<Page<InvoiceSummary>>(
            new Uri("/invoices/exceptions?limit=200", UriKind.Relative), Scenario.Json))!;
        queue.Items.ShouldContain(summary => summary.Id == second.Id);

        await (await ClearAsync(both, second.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "invoice.self_approval");
        InvoiceView cleared = await Scenario.ReadAsync<InvoiceView>(await ClearAsync(TestUsers.Aaron, second.Id), HttpStatusCode.OK);

        cleared.Status.ShouldBe(InvoiceStatus.Payable);
        cleared.DuplicateClearedBy.ShouldBe(TestUsers.Aaron.Id);
    }

    [Fact]
    public async Task A_number_the_supplier_already_used_is_refused_however_it_is_written()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        await _given.CaptureAsync(TestUsers.Alice, Scenario.Invoice(supplier, order, "INV-0042", Scenario.Today, (1, 1, 10m)));

        HttpResponseMessage again = await _given.PostCaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, order, "inv 42", Scenario.Today, (1, 2, 10m)));

        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invoice.duplicate");
    }

    [Fact]
    public async Task Two_captures_of_one_number_at_the_same_moment_create_one_invoice()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        CaptureInvoiceRequest request = Scenario.Invoice(supplier, order, "INV-RACE", Scenario.Today, (1, 1, 10m));

        // Both have passed the application's own duplicate check when the lock goes, so only the index can decide.
        HttpResponseMessage[] responses = await Race.AtAsync(
            _given.Host.Database,
            "lock table invoices in share row exclusive mode",
            () => _given.PostCaptureAsync(TestUsers.Alice, request),
            () => _given.PostCaptureAsync(TestUsers.Alice, request));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        await responses.Single(response => response.StatusCode != HttpStatusCode.Created)
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "invoice.duplicate");
        (await _given.DbAsync(db => db.Invoices.CountAsync(invoice => invoice.SupplierId == supplier))).ShouldBe(1);
    }

    [Fact]
    public async Task A_capture_repeated_with_its_id_returns_the_same_invoice_and_different_content_is_refused()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        CaptureInvoiceRequest request = Scenario.Invoice(supplier, order, "INV-RETRY", Scenario.Today, (1, 2, 10m)) with { Id = Guid.CreateVersion7() };

        InvoiceView first = await _given.CaptureAsync(TestUsers.Alice, request);
        InvoiceView repeated = await _given.CaptureAsync(TestUsers.Alice, request);

        repeated.Id.ShouldBe(request.Id!.Value);
        repeated.Id.ShouldBe(first.Id);
        (await _given.DbAsync(db => db.Invoices.CountAsync(invoice => invoice.SupplierId == supplier))).ShouldBe(1);
        await (await _given.PostCaptureAsync(TestUsers.Alice, request with { Total = 21m, Lines = [new CaptureInvoiceLineRequest(1, 2, 10.5m)] }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }

    [Fact]
    public async Task A_total_that_is_not_the_sum_of_the_lines_is_refused_with_its_code() =>
        await (await _given.PostCaptureAsync(
                TestUsers.Alice,
                Scenario.Invoice(Guid.CreateVersion7(), Guid.CreateVersion7(), "INV-SUM", Scenario.Today, (1, 2, 10m)) with { Total = 25m }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "invoice.total_mismatch");

    private Task<HttpResponseMessage> AcceptAsync(Actor actor, Guid invoiceId) =>
        _given.As(actor).PostAsJsonAsync(
            new Uri($"/invoices/{invoiceId}/accept-price-variance", UriKind.Relative),
            new AcceptPriceVarianceRequest("Freight surcharge agreed with the buyer."),
            Scenario.Json);

    private Task<HttpResponseMessage> ClearAsync(Actor actor, Guid invoiceId) =>
        _given.As(actor).PostAsync(new Uri($"/invoices/{invoiceId}/clear-suspected-duplicate", UriKind.Relative), null);
}
