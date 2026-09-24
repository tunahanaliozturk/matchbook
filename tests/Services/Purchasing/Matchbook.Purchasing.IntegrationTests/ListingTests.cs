using System.Net;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListPurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

/// <summary>
/// Listing, alone in its class so its host sees only the orders this test creates and the counts can be exact. It
/// shared a host with the lookup tests once, and failed whenever one of them happened to run first and leave a draft.
/// </summary>
public sealed class ListingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
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
}
