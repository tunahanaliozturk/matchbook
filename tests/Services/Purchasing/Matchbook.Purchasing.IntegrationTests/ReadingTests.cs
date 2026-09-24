using System.Net;
using Matchbook.Contracts.Suppliers;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListPurchaseOrders;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

/// <summary>Lookups by id and by requisition, what an unknown one answers, and the supplier name orders carry.</summary>
public sealed class ReadingTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    private readonly HttpClient _audrey = fixture.ClientFor(TestUsers.Audrey);
    private readonly HttpClient _bruno = fixture.ClientFor(TestUsers.Bruno);

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
    public async Task An_order_names_its_supplier_from_the_newest_snapshot_in_reads_lists_and_answers_to_commands()
    {
        Guid supplierId = Guid.CreateVersion7();
        await fixture.PublishAndWaitAsync(Messages.Supplier(supplierId, 1, SupplierStatus.Active, "Acme Office Supplies GmbH"));

        PurchaseOrderView draft = await fixture.DraftAsync(supplierId, (1m, 1m));
        draft.SupplierName.ShouldBe("Acme Office Supplies GmbH");

        // A rename reaches orders already drafted, because the name is read from the copy, not kept on the order.
        await fixture.PublishAndWaitAsync(Messages.Supplier(supplierId, 2, SupplierStatus.Active, "Acme Office GmbH"));

        (await fixture.GetAsync(draft.Id)).SupplierName.ShouldBe("Acme Office GmbH");
        PurchaseOrderPage drafts = await (await _audrey.GetAsync(Routes.Orders("?status=Draft&limit=200")))
            .ReadAsync<PurchaseOrderPage>(HttpStatusCode.OK);
        drafts.Items.Single(item => item.Id == draft.Id).SupplierName.ShouldBe("Acme Office GmbH");
        (await (await _bruno.PutJsonAsync(Routes.Line(draft.Id, 1), new { quantity = 2m, unitPrice = 1m }))
            .ReadAsync<PurchaseOrderView>(HttpStatusCode.OK)).SupplierName.ShouldBe("Acme Office GmbH");
    }

    [Fact]
    public async Task An_order_for_a_supplier_purchasing_has_not_heard_of_has_no_supplier_name_and_still_lists()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(Guid.CreateVersion7(), (1m, 1m));

        draft.SupplierName.ShouldBeNull();
        PurchaseOrderPage drafts = await (await _audrey.GetAsync(Routes.Orders("?status=Draft&limit=200")))
            .ReadAsync<PurchaseOrderPage>(HttpStatusCode.OK);
        drafts.Items.Single(item => item.Id == draft.Id).SupplierName.ShouldBeNull();
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
