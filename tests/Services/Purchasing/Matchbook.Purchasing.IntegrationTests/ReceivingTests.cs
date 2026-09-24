using System.Net;
using System.Text.Json;
using Matchbook.Contracts.Purchasing;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

public sealed class ReceivingTests(PurchasingFixture fixture, ITestOutputHelper output) : IClassFixture<PurchasingFixture>
{
    private readonly EventProbe _probe = fixture.Probe;
    private readonly HttpClient _rosa = fixture.ClientFor(TestUsers.Rosa);

    private static object Receipt(Guid? id, params (int Line, decimal Quantity)[] lines) =>
        new { id, lines = lines.Select(static line => new { lineNumber = line.Line, quantity = line.Quantity }) };

    [Fact]
    public async Task Rosa_records_a_receipt_and_goods_received_carries_that_receipts_quantities()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((10m, 12.50m), (4m, 99.99m));
        Guid firstId = Guid.CreateVersion7();

        HttpResponseMessage response = await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(firstId, (1, 6m), (2, 1m)));

        GoodsReceiptView first = await response.ReadAsync<GoodsReceiptView>(HttpStatusCode.Created);
        first.Id.ShouldBe(firstId);
        first.ReceivedBy.ShouldBe(TestUsers.Rosa.Id);
        response.Headers.Location.ShouldBe(new Uri($"receipts/{firstId}", UriKind.Relative));

        GoodsReceiptView second = await (await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(null, (1, 3m))))
            .ReadAsync<GoodsReceiptView>(HttpStatusCode.Created);

        GoodsReceived secondEvent = await _probe.WaitForAsync<GoodsReceived>(message => message.ReceiptId == second.Id);
        secondEvent.PurchaseOrderId.ShouldBe(order.Id);
        secondEvent.ReceivedBy.ShouldBe(TestUsers.Rosa.Id);
        secondEvent.Lines.ShouldBe([new ReceiptLine(1, 3m)]);
        (await _probe.WaitForAsync<GoodsReceived>(message => message.ReceiptId == firstId)).Lines
            .ShouldBe([new ReceiptLine(1, 6m), new ReceiptLine(2, 1m)]);

        PurchaseOrderView after = await fixture.GetAsync(order.Id);
        after.Lines.Select(static line => line.ReceivedQuantity).ShouldBe([9m, 1m]);
        (await (await _rosa.GetAsync(Routes.Receipts(order.Id))).ReadAsync<GoodsReceiptView[]>(HttpStatusCode.OK))
            .Select(static receipt => receipt.Id).ShouldBe([firstId, second.Id]);
        (await (await _rosa.GetAsync(Routes.Receipt(order.Id, firstId))).ReadAsync<GoodsReceiptView>(HttpStatusCode.OK))
            .ShouldBeEquivalentTo(first);
    }

    [Fact]
    public async Task The_buyer_who_issued_the_order_may_not_receive_it_even_holding_the_receiver_role()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((5m, 1m));
        var brunoWhoAlsoReceives = new Actor(TestUsers.Bruno.Id, "bruno", new HashSet<string> { Roles.Buyer, Roles.Receiver });

        HttpResponseMessage response = await fixture.ClientFor(brunoWhoAlsoReceives)
            .PostJsonAsync(Routes.Receipts(order.Id), Receipt(null, (1, 1m)));

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "purchase_order.receiver_is_buyer");
        (await fixture.GetAsync(order.Id)).Lines[0].ReceivedQuantity.ShouldBe(0m);
    }

    [Fact]
    public async Task Receiving_more_than_is_open_is_refused()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((5m, 1m));
        await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(null, (1, 4m)));

        await (await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(null, (1, 1.001m))))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "purchase_order.over_receipt");
    }

    [Fact]
    public async Task A_receipt_retried_with_its_id_returns_the_first_answer_and_records_nothing_more()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((5m, 1m));
        Guid receiptId = Guid.CreateVersion7();

        HttpResponseMessage first = await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(receiptId, (1, 2m)));
        HttpResponseMessage retry = await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(receiptId, (1, 2m)));

        retry.StatusCode.ShouldBe(first.StatusCode);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await fixture.GetAsync(order.Id)).Lines[0].ReceivedQuantity.ShouldBe(2m);
        (await fixture.CountAfterSettlingAsync<GoodsReceived>(message => message.ReceiptId == receiptId)).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_id_for_a_different_receipt_is_refused()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((5m, 1m));
        Guid receiptId = Guid.CreateVersion7();
        await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(receiptId, (1, 2m)));

        await (await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(receiptId, (1, 3m))))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }

    [Fact]
    public async Task A_receipt_missing_a_quantity_or_with_an_empty_id_is_a_bad_request_naming_the_field()
    {
        PurchaseOrderView order = await fixture.IssuedAsync((5m, 1m));

        HttpResponseMessage missing = await _rosa.PostJsonAsync(Routes.Receipts(order.Id), new { lines = new[] { new { lineNumber = 1 } } });
        HttpResponseMessage empty = await _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(Guid.Empty, (1, 1m)));

        (await ErrorFieldsAsync(missing)).ShouldContain(field => field.EndsWith("Quantity", StringComparison.OrdinalIgnoreCase));
        (await ErrorFieldsAsync(empty)).ShouldContain(field => field.EndsWith("Id", StringComparison.OrdinalIgnoreCase));
        (await fixture.GetAsync(order.Id)).Lines[0].ReceivedQuantity.ShouldBe(0m);
    }

    [Fact]
    public async Task Two_receipts_at_once_that_together_pass_the_ordered_quantity_give_one_success_and_one_refusal()
    {
        const int Rounds = 12;
        PurchaseOrderView order = await fixture.IssuedAsync([.. Enumerable.Repeat((10m, 1m), Rounds)]);
        Dictionary<string, int> refusals = [];

        for (int line = 1; line <= Rounds; line++)
        {
            // Each alone fits (6 of 10); together they would not. Both are sent at once, against the same order row.
            HttpResponseMessage[] responses = await Task.WhenAll(
                _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(null, (line, 6m))),
                _rosa.PostJsonAsync(Routes.Receipts(order.Id), Receipt(null, (line, 6m))));

            responses.Count(static response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            HttpResponseMessage refused = responses.Single(static response => response.StatusCode != HttpStatusCode.Created);
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            string code = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;
            code.ShouldBeOneOf("purchase_order.over_receipt", "concurrency.conflict");
            refusals[code] = refusals.GetValueOrDefault(code) + 1;
        }

        (await fixture.GetAsync(order.Id)).Lines.ShouldAllBe(static line => line.ReceivedQuantity == 6m);
        (await fixture.CountAfterSettlingAsync<GoodsReceived>(message => message.PurchaseOrderId == order.Id)).ShouldBe(Rounds);
        output.WriteLine($"Refusals over {Rounds} races: {string.Join(", ", refusals.Select(static pair => $"{pair.Key} {pair.Value}"))}");
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        return [.. JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateObject().Select(static field => field.Name)];
    }
}
