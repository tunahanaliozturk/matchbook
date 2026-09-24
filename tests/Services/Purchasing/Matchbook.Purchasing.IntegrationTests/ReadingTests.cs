using System.Net;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Purchasing.Domain;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

/// <summary>Lookups by id and by requisition, and what an unknown one answers.</summary>
public sealed class ReadingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    private readonly HttpClient _audrey = fixture.ClientFor(TestUsers.Audrey);

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
