using System.Net;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

/// <summary>Lists and lookups. This class's host sees only the orders these tests create, so counts are exact.</summary>
public sealed class ReadingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    private readonly HttpClient _audrey = fixture.ClientFor(TestUsers.Audrey);

    [Fact]
    public async Task Orders_of_one_status_come_newest_first_a_page_at_a_time_until_the_cursor_runs_out()
    {
        Guid supplierId = await fixture.ActiveSupplierAsync();
        List<Guid> drafted = [];

        for (int i = 0; i < 5; i++)
        {
            drafted.Add((await fixture.DraftAsync(supplierId, (1m, 1m))).Id);
        }

        PurchaseOrderView cancelled = await fixture.DraftAsync(supplierId, (1m, 1m));
        (await fixture.ClientFor(TestUsers.Bruno).PostAsync(Routes.Cancel(cancelled.Id), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        List<Guid> seen = [];
        Guid? after = null;
        int pages = 0;

        do
        {
            string cursor = after is { } id ? $"&after={id}" : "";
            PurchaseOrderPage page = await (await _audrey.GetAsync(Routes.Orders($"?status=Draft&limit=2{cursor}")))
                .ReadAsync<PurchaseOrderPage>(HttpStatusCode.OK);

            page.Items.Count.ShouldBeLessThanOrEqualTo(2);
            page.Items.ShouldAllBe(static item => item.Status == PurchaseOrderStatus.Draft);
            seen.AddRange(page.Items.Select(static item => item.Id));
            after = page.Next;
            pages++;
        }
        while (after is not null);

        seen.ShouldBe([.. Enumerable.Reverse(drafted)]);
        pages.ShouldBe(3);

        PurchaseOrderPage cancelledPage = await (await _audrey.GetAsync(Routes.Orders("?status=Cancelled")))
            .ReadAsync<PurchaseOrderPage>(HttpStatusCode.OK);
        cancelledPage.Items.Select(static item => item.Id).ShouldBe([cancelled.Id]);
        cancelledPage.Next.ShouldBeNull();
    }

    [Fact]
    public async Task The_order_for_a_requisition_is_found_by_the_requisition_id_and_is_a_404_before_it_is_drafted()
    {
        await (await _audrey.GetAsync(Routes.ForRequisition(Guid.CreateVersion7())))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "purchase_order.not_found");

        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (1m, 1m));

        (await (await _audrey.GetAsync(Routes.ForRequisition(draft.RequisitionId))).ReadAsync<PurchaseOrderView>(HttpStatusCode.OK))
            .Id.ShouldBe(draft.Id);
    }

    [Fact]
    public async Task An_unknown_order_or_receipt_is_a_404_with_a_code()
    {
        await (await _audrey.GetAsync(Routes.Order(Guid.CreateVersion7())))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "purchase_order.not_found");
        await (await _audrey.GetAsync(Routes.Receipts(Guid.CreateVersion7())))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "purchase_order.not_found");
        await (await _audrey.GetAsync(Routes.Receipt(Guid.CreateVersion7(), Guid.CreateVersion7())))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "goods_receipt.not_found");
    }
}
