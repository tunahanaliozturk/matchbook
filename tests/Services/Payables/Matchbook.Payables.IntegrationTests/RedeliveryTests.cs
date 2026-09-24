using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Suppliers;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// Every consumed event delivered again changes nothing. Each is sent twice under one message id, which the inbox
/// drops, and once more under a new id, which only the consumer's own idempotency can stop.
/// </summary>
public sealed class RedeliveryTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private readonly Scenario _given = new(fixture);

    [Fact]
    public async Task A_receipt_delivered_again_is_counted_once()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        InvoiceView invoice = await _given.CaptureAsync(TestUsers.Alice, Scenario.Invoice(supplier, order, "INV-R1", Scenario.Today, (1, 10, 10m)));
        GoodsReceived half = Scenario.Receipt(order, (1, 5));

        await DeliverRepeatedlyAsync(half);

        // Counted twice, five and five would have matched the invoice for ten.
        (await _given.InvoiceAsync(invoice.Id)).Status.ShouldBe(InvoiceStatus.AwaitingReceipt);
        (await _given.DbAsync(db => db.Receipts.CountAsync(receipt => receipt.PurchaseOrderId == order))).ShouldBe(1);

        await _given.Probe.PublishAsync(Scenario.Receipt(order, (1, 5)));
        await _given.InvoiceInAsync(invoice.Id, InvoiceStatus.Payable);
    }

    [Fact]
    public async Task An_order_delivered_again_matches_its_invoices_once()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid orderId = Guid.CreateVersion7();
        await _given.ReceiveAsync(orderId, (1, 2));
        InvoiceView invoice = await _given.CaptureAsync(TestUsers.Alice, Scenario.Invoice(supplier, orderId, "INV-R2", Scenario.Today, (1, 2, 30m)));

        await DeliverRepeatedlyAsync(Scenario.OrderIssued(orderId, supplier, (1, 2, 30m)));

        await _given.InvoiceInAsync(invoice.Id, InvoiceStatus.Payable);
        _given.Probe.Received<InvoiceMatched>().Count(matched => matched.InvoiceId == invoice.Id).ShouldBe(1);
        PurchaseOrder order = await _given.DbAsync(db => db.PurchaseOrders.AsNoTracking().SingleAsync(candidate => candidate.Id == orderId));
        order.Lines.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_closure_delivered_again_changes_nothing_and_a_later_one_does_not_overwrite_it()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = await _given.OrderAsync(supplier, (1, 10, 10m));
        InvoiceView invoice = await _given.CaptureAsync(TestUsers.Alice, Scenario.Invoice(supplier, order, "INV-R3", Scenario.Today, (1, 1, 10m)));
        var cancelled = new PurchaseOrderClosed(order, Guid.CreateVersion7(), PurchaseOrderCloseReason.Cancelled, DateTimeOffset.UtcNow);

        await DeliverRepeatedlyAsync(cancelled);
        await _given.Probe.PublishAsync(cancelled with { Reason = PurchaseOrderCloseReason.Completed });
        await Task.Delay(Settle);

        (await _given.InvoiceInAsync(invoice.Id, InvoiceStatus.Rejected)).Reason.ShouldBe(MatchReason.OrderCancelled);
        (await _given.DbAsync(db => db.PurchaseOrders.AsNoTracking().SingleAsync(candidate => candidate.Id == order)))
            .CloseReason.ShouldBe(PurchaseOrderCloseReason.Cancelled);
    }

    [Fact]
    public async Task A_supplier_snapshot_delivered_again_or_out_of_order_leaves_the_newest_in_place()
    {
        Guid supplier = Guid.CreateVersion7();
        SupplierChanged newest = Scenario.SupplierChanged(supplier, "GB82WEST12345698765432", paymentTermsDays: 45, version: 2);
        SupplierChanged older = Scenario.SupplierChanged(supplier, "NL91ABNA0417164300", paymentTermsDays: 10, version: 1, active: false);

        await DeliverRepeatedlyAsync(newest);
        await _given.Probe.PublishAsync(older);
        await Task.Delay(Settle);

        Supplier copy = await _given.DbAsync(db => db.Suppliers.AsNoTracking().SingleAsync(candidate => candidate.Id == supplier));
        copy.Version.ShouldBe(2);
        copy.PaymentTermsDays.ShouldBe(45);
        copy.IsActive.ShouldBeTrue();
        copy.Account!.IbanLastFour.ShouldBe("5432");
    }

    /// <summary>Delivers the message three times: twice under one message id, then under another.</summary>
    private async Task DeliverRepeatedlyAsync<T>(T message)
        where T : class
    {
        Guid messageId = Guid.NewGuid();
        await _given.Probe.PublishAsync(message, messageId);
        await _given.Probe.PublishAsync(message, messageId);
        await _given.Probe.PublishAsync(message, Guid.NewGuid());
        await Task.Delay(Settle);
    }
}
