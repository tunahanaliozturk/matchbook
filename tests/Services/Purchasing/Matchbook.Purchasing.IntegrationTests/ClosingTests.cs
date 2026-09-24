using System.Net;
using Matchbook.Contracts.Purchasing;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

public sealed class ClosingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    private readonly HttpClient _bruno = fixture.ClientFor(TestUsers.Bruno);
    private readonly HttpClient _rosa = fixture.ClientFor(TestUsers.Rosa);

    private Task<HttpResponseMessage> ReceiveAsync(Guid orderId, decimal quantity) =>
        _rosa.PostJsonAsync(Routes.Receipts(orderId), new { lines = new[] { new { lineNumber = 1, quantity } } });

    [Fact]
    public async Task A_buyer_short_closes_an_issued_order_budgets_is_told_and_nothing_more_is_received()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((10m, 5m));
        (await ReceiveAsync(order.Id, 4m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        PurchaseOrderView closed = await (await _bruno.PostAsync(Routes.ShortClose(order.Id), null)).ReadAsync<PurchaseOrderView>(HttpStatusCode.OK);

        closed.Status.ShouldBe(PurchaseOrderStatus.ShortClosed);
        closed.ClosedBy.ShouldBe(TestUsers.Bruno.Id);
        (await fixture.Probe.WaitForAsync<PurchaseOrderClosed>(message => message.PurchaseOrderId == order.Id))
            .Reason.ShouldBe(PurchaseOrderCloseReason.ShortClosed);
        await (await ReceiveAsync(order.Id, 1m)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.not_issued");
    }

    [Fact]
    public async Task A_draft_is_cancelled_and_budgets_is_told_to_release_the_reservation()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (1m, 10m));

        (await (await _bruno.PostAsync(Routes.Cancel(draft.Id), null)).ReadAsync<PurchaseOrderView>(HttpStatusCode.OK))
            .Status.ShouldBe(PurchaseOrderStatus.Cancelled);

        PurchaseOrderClosed closed = await fixture.Probe.WaitForAsync<PurchaseOrderClosed>(message => message.PurchaseOrderId == draft.Id);
        closed.Reason.ShouldBe(PurchaseOrderCloseReason.Cancelled);
        closed.RequisitionId.ShouldBe(draft.RequisitionId);
    }

    [Fact]
    public async Task An_issued_order_with_nothing_received_can_be_cancelled()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((3m, 3m));

        (await (await _bruno.PostAsync(Routes.Cancel(order.Id), null)).ReadAsync<PurchaseOrderView>(HttpStatusCode.OK))
            .Status.ShouldBe(PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public async Task An_order_with_goods_received_can_be_short_closed_but_not_cancelled()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((3m, 3m));
        (await ReceiveAsync(order.Id, 1m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await (await _bruno.PostAsync(Routes.Cancel(order.Id), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.goods_received");
    }

    [Fact]
    public async Task A_draft_cannot_be_short_closed_and_a_closed_order_cannot_be_closed_again()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (1m, 10m));

        await (await _bruno.PostAsync(Routes.ShortClose(draft.Id), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.not_issued");

        (await _bruno.PostAsync(Routes.Cancel(draft.Id), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await _bruno.PostAsync(Routes.Cancel(draft.Id), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.closed");
    }

    [Fact]
    public async Task Amending_a_line_below_zero_or_on_an_issued_order_is_refused_with_the_rule()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (1m, 10m));
        await (await _bruno.PutJsonAsync(Routes.Line(draft.Id, 1), new { quantity = -1m, unitPrice = 10m }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "purchase_order.negative_quantity");

        PurchaseOrderView issued = await fixture.IssuedAsync((1m, 10m));
        await (await _bruno.PutJsonAsync(Routes.Line(issued.Id, 1), new { quantity = 2m, unitPrice = 10m }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.not_draft");
    }
}
