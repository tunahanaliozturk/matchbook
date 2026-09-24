using System.Text.Json;
using Matchbook.Stack;
using Matchbook.Testing;

namespace Matchbook.SystemTests;

public sealed class PurchaseToPayTests(StackFixture stack)
{
    [Fact]
    public async Task A_purchase_goes_from_request_to_payment_and_every_ledger_agrees()
    {
        World world = await World.CreateAsync(stack.Gateway, allotted: 10_000m);

        Purchase purchase = await Purchase.ToPayableAsync(stack.Gateway, world, quantity: 8, unitPrice: 312.50m, TimeSpan.FromSeconds(30));
        JsonElement run = await PaymentRun.DraftAndReleaseAsync(stack.Gateway);

        run.Text("status").ShouldBe("Released");
        run.GetProperty("creditors").EnumerateArray()
            .Single(creditor => creditor.Id("supplierId") == world.Supplier)
            .Text("status").ShouldBe("Paid");

        string file = await stack.Gateway.GetTextAsync(TestUsers.Tess, $"payment-runs/{run.Id()}/file");
        file.ShouldContain("urn:iso:std:iso:20022:tech:xsd:pain.001.001.09");
        file.ShouldContain(purchase.InvoiceNumber);

        IReadOnlyList<Discrepancy> discrepancies = await stack.ReconcileAsync(TimeSpan.FromSeconds(60));

        (await stack.Gateway.GetAsync(TestUsers.Alice, $"invoices/{purchase.Invoice}")).Text("status").ShouldBe("Paid");
        (await stack.Gateway.GetAsync(TestUsers.Bruno, $"purchase-orders/{purchase.Order}")).Text("status").ShouldBe("Completed");
        (await stack.Gateway.GetAsync(TestUsers.Rita, $"requisitions/{purchase.Requisition}")).Text("status").ShouldBe("Closed");

        JsonElement budget = await stack.Gateway.GetAsync(TestUsers.Bob, $"budgets/{world.Budget}");
        budget.Number("reserved").ShouldBe(0m);
        budget.Number("committed").ShouldBe(0m);
        budget.Number("actual").ShouldBe(2_500m);
        budget.Number("available").ShouldBe(7_500m);

        discrepancies.ShouldBeEmpty(string.Join(Environment.NewLine, discrepancies));
    }
}
