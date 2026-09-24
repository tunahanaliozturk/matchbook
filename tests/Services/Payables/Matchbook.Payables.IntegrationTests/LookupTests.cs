using System.Net;
using System.Net.Http.Json;
using Matchbook.Contracts.Purchasing;
using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Application.Features.Invoices.Queries.ListBillablePurchaseOrders;
using Matchbook.Payables.Application.Features.Invoices.Queries.ListBillableSuppliers;
using Matchbook.Payables.Application.Features.PaymentRuns;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// The reads the console needs beyond the documents themselves: what a clerk can bill when capturing an invoice, the
/// names people know suppliers and orders by, and the list of payment runs.
/// </summary>
public sealed class LookupTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>
{
    private readonly Scenario _given = new(fixture);

    [Fact]
    public async Task The_capture_form_offers_only_suppliers_and_orders_an_invoice_can_still_bill()
    {
        Guid billed = await _given.SupplierAsync();
        Guid open = await _given.OrderAsync(billed, (2, 5, 20m), (1, 10, 12.5m));
        Guid cancelled = await ClosedOrderAsync(billed, PurchaseOrderCloseReason.Cancelled);
        Guid completed = await ClosedOrderAsync(billed, PurchaseOrderCloseReason.Completed);
        Guid shortClosed = await ClosedOrderAsync(billed, PurchaseOrderCloseReason.ShortClosed);
        Guid onlyCancelled = await _given.SupplierAsync();
        await ClosedOrderAsync(onlyCancelled, PurchaseOrderCloseReason.Cancelled);
        Guid withoutOrders = await _given.SupplierAsync();

        Page<BillableSupplier> suppliers = await GetAsync<Page<BillableSupplier>>("/invoices/suppliers?limit=200");
        Page<BillablePurchaseOrder> orders = await GetAsync<Page<BillablePurchaseOrder>>($"/invoices/purchase-orders?supplierId={billed}");

        suppliers.Items.Select(supplier => supplier.Id).ShouldContain(billed);
        suppliers.Items.Select(supplier => supplier.Id).ShouldNotContain(onlyCancelled);
        suppliers.Items.Select(supplier => supplier.Id).ShouldNotContain(withoutOrders);
        suppliers.Items.Single(supplier => supplier.Id == billed).LegalName.ShouldBe("ACME Industrial Supplies GmbH");

        // A short-closed order still owes what was received before the close; cancelled and completed ones owe nothing.
        orders.Items.Select(order => order.Id).ShouldBe([shortClosed, open]);
        orders.Items.Select(order => order.Id).ShouldNotContain(cancelled);
        orders.Items.Select(order => order.Id).ShouldNotContain(completed);
        orders.Items[^1].Lines.ShouldBe([new BillablePurchaseOrderLine(1, 10, 12.5m), new BillablePurchaseOrderLine(2, 5, 20m)]);
    }

    [Fact]
    public async Task An_invoice_names_its_supplier_and_order_once_their_copies_have_arrived()
    {
        Guid supplier = await _given.SupplierAsync();
        PurchaseOrderIssued issued = Scenario.OrderIssued(Guid.CreateVersion7(), supplier, (1, 1, 10m));
        await _given.Probe.PublishAsync(issued);
        await _given.OrderArrivedAsync(issued.PurchaseOrderId);

        InvoiceView captured = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(supplier, issued.PurchaseOrderId, "INV-NAMED-1", Scenario.Today, (1, 1, 10m)));
        InvoiceView unknown = await _given.CaptureAsync(
            TestUsers.Alice,
            Scenario.Invoice(Guid.CreateVersion7(), Guid.CreateVersion7(), "INV-NAMED-2", Scenario.Today, (1, 1, 10m)));

        captured.SupplierName.ShouldBe("ACME Industrial Supplies GmbH");
        captured.PurchaseOrderNumber.ShouldBe(issued.Number);
        (await _given.InvoiceAsync(captured.Id)).PurchaseOrderNumber.ShouldBe(issued.Number);
        unknown.SupplierName.ShouldBeNull();
        unknown.PurchaseOrderNumber.ShouldBeNull();

        Page<InvoiceSummary> listed = await GetAsync<Page<InvoiceSummary>>("/invoices?limit=200");
        listed.Items.Single(invoice => invoice.Id == captured.Id).SupplierName.ShouldBe("ACME Industrial Supplies GmbH");
        listed.Items.Single(invoice => invoice.Id == unknown.Id).SupplierName.ShouldBeNull();
    }

    [Fact]
    public async Task Payment_runs_are_listed_newest_first_page_by_cursor_and_filter_by_status()
    {
        Guid supplier = await _given.SupplierAsync();
        await _given.PayableInvoiceAsync(supplier, 25m);
        PaymentRunView cancelled = await _given.DraftAsync(TestUsers.Tess);
        (await _given.As(TestUsers.Tess).PostAsync(new Uri($"/payment-runs/{cancelled.Id}/cancel", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        PaymentRunView draft = await _given.DraftAsync(TestUsers.Tess);

        Page<PaymentRunSummary> first = await GetAsync<Page<PaymentRunSummary>>("/payment-runs?limit=1", TestUsers.Audrey);
        Page<PaymentRunSummary> second = await GetAsync<Page<PaymentRunSummary>>($"/payment-runs?limit=1&after={first.Next}", TestUsers.Audrey);
        Page<PaymentRunSummary> drafts = await GetAsync<Page<PaymentRunSummary>>("/payment-runs?status=Draft", TestUsers.Trevor);

        first.Items.ShouldHaveSingleItem().Id.ShouldBe(draft.Id);
        second.Items.ShouldHaveSingleItem().Id.ShouldBe(cancelled.Id);
        second.Items[0].Status.ShouldBe(PaymentRunStatus.Cancelled);
        drafts.Items.Select(run => run.Id).ShouldBe([draft.Id]);
        drafts.Items[0].DraftedBy.ShouldBe(TestUsers.Tess.Id);
        drafts.Items[0].ItemCount.ShouldBe(draft.ItemCount);
        drafts.Items[0].Total.ShouldBe(draft.Total);
        drafts.Items[0].ReleasedBy.ShouldBeNull();
    }

    private async Task<Guid> ClosedOrderAsync(Guid supplierId, string reason)
    {
        Guid order = await _given.OrderAsync(supplierId, (1, 1, 10m));
        await _given.Probe.PublishAsync(new PurchaseOrderClosed(order, Guid.CreateVersion7(), reason, DateTimeOffset.UtcNow));
        await _given.UntilAsync(db => db.PurchaseOrders.AnyAsync(candidate => candidate.Id == order && candidate.ClosedAt != null));
        return order;
    }

    private async Task<T> GetAsync<T>(string path, Actor? actor = null) =>
        (await _given.As(actor ?? TestUsers.Alice).GetFromJsonAsync<T>(new Uri(path, UriKind.Relative), Scenario.Json))!;
}
