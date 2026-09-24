using System.Net;
using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Purchasing;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

public sealed class InvoicingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    private readonly HttpClient _rosa = fixture.ClientFor(TestUsers.Rosa);

    private async Task<PurchaseOrderView> ReceivedInFullAsync()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((10m, 12.50m), (4m, 99.99m));
        HttpResponseMessage response = await _rosa.PostJsonAsync(
            Routes.Receipts(order.Id),
            new { lines = new[] { new { lineNumber = 1, quantity = 10m }, new { lineNumber = 2, quantity = 4m } } });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return order;
    }

    [Fact]
    public async Task A_matched_invoice_counts_as_invoiced_and_the_same_invoice_again_changes_nothing()
    {
        PurchaseOrderView order = await ReceivedInFullAsync();
        InvoiceMatched invoice = Messages.Invoice(Guid.CreateVersion7(), order.Id, order.SupplierId, (1, 5m));
        Guid messageId = Guid.NewGuid();

        await fixture.PublishAndWaitAsync(invoice, messageId);
        (await fixture.GetAsync(order.Id)).Lines[0].InvoicedQuantity.ShouldBe(5m);

        // Once as a redelivery the inbox catches, once as a new message only the invoice id can catch.
        await fixture.Probe.PublishAsync(invoice, messageId);
        await fixture.PublishAndWaitAsync(invoice);

        PurchaseOrderView after = await fixture.GetAsync(order.Id);
        after.Lines.Select(static line => line.InvoicedQuantity).ShouldBe([5m, 0m]);
        after.Status.ShouldBe(PurchaseOrderStatus.Issued);
    }

    [Fact]
    public async Task An_order_received_and_invoiced_in_full_closes_completed_and_says_so_once()
    {
        PurchaseOrderView order = await ReceivedInFullAsync();
        InvoiceMatched last = Messages.Invoice(Guid.CreateVersion7(), order.Id, order.SupplierId, (1, 4m), (2, 4m));

        await fixture.PublishAndWaitAsync(Messages.Invoice(Guid.CreateVersion7(), order.Id, order.SupplierId, (1, 6m)));
        await fixture.Probe.PublishAsync(last);

        PurchaseOrderClosed closed = await fixture.Probe.WaitForAsync<PurchaseOrderClosed>(message => message.PurchaseOrderId == order.Id);
        closed.Reason.ShouldBe(PurchaseOrderCloseReason.Completed);
        closed.RequisitionId.ShouldBe(order.RequisitionId);

        await fixture.PublishAndWaitAsync(last);
        PurchaseOrderView completed = await fixture.GetAsync(order.Id);
        completed.Status.ShouldBe(PurchaseOrderStatus.Completed);
        completed.Lines.ShouldAllBe(static line => line.InvoicedQuantity == line.Quantity && line.ReceivedQuantity == line.Quantity);
        (await fixture.CountAfterSettlingAsync<PurchaseOrderClosed>(message => message.PurchaseOrderId == order.Id)).ShouldBe(1);
    }
}
