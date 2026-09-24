using System.Globalization;
using System.Text.Json;
using Matchbook.Testing;

namespace Matchbook.SystemTests;

/// <summary>What every purchase in a test buys from and charges to: one active supplier and one fresh budget.</summary>
internal sealed record World(Guid Supplier, int PaymentTermsDays, string CostCentre, Guid Budget)
{
    public static async Task<World> CreateAsync(Gateway gateway, decimal allotted)
    {
        const int terms = 30;
        string run = Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();

        JsonElement supplier = await gateway.PostAsync(TestUsers.Sam, "suppliers", new
        {
            id = Guid.CreateVersion7(),
            legalName = $"System Test {run} GmbH",
            taxId = $"DE{Random.Shared.NextInt64(100_000_000, 999_999_999)}",
            countryCode = "DE",
            paymentTermsDays = terms,
            contactEmail = "ap@system-test.example",
        });

        Guid supplierId = supplier.Id();
        Guid accountId = Guid.CreateVersion7();

        await gateway.PostAsync(TestUsers.Sam, $"suppliers/{supplierId}/bank-accounts", new
        {
            id = accountId,
            iban = "DE89370400440532013000",
            bic = "DEUTDEFF",
            accountHolder = $"System Test {run} GmbH",
        });

        // Four eyes: sam proposes and submits, sofia approves and activates.
        await gateway.PostAsync(TestUsers.Sofia, $"suppliers/{supplierId}/bank-accounts/{accountId}/approve");
        await gateway.PostAsync(TestUsers.Sam, $"suppliers/{supplierId}/submit");
        await gateway.PostAsync(TestUsers.Sofia, $"suppliers/{supplierId}/activate");

        string costCentre = $"SYS-{run}";

        await gateway.PostAsync(TestUsers.Bob, "cost-centres", new
        {
            id = Guid.CreateVersion7(),
            code = costCentre,
            name = $"System test {run}",
            managerId = TestUsers.Mark.Id,
        });

        JsonElement budget = await gateway.PostAsync(TestUsers.Bob, "budgets", new
        {
            id = Guid.CreateVersion7(),
            costCentreCode = costCentre,
            fiscalYear = DateTime.UtcNow.Year,
            allotted,
        });

        return new World(supplierId, terms, costCentre, budget.Id());
    }
}

/// <summary>One purchase, from request to a payable invoice, as the people who own each step would take it.</summary>
internal sealed record Purchase(Guid Requisition, Guid Order, Guid Invoice, string InvoiceNumber, decimal Amount)
{
    /// <summary>
    /// Rita requests, mark approves, bruno orders, rosa receives, alice captures the invoice; returns once the
    /// invoice has passed the three-way match. Every create carries an id the client chose and every command is
    /// checked against the state before it is repeated, so the whole walk survives requests that are sent twice.
    /// </summary>
    public static async Task<Purchase> ToPayableAsync(Gateway gateway, World world, decimal quantity, decimal unitPrice, TimeSpan patience)
    {
        Guid requisitionId = Guid.CreateVersion7();
        // Callers pass whole quantities and prices in cents, so the amount needs no rounding rule of its own.
        decimal amount = quantity * unitPrice;

        // The supplier and cost centre reach Requisitions as events; until both have, it answers *_unknown.
        await Steps.UntilKnownAsync(() => gateway.PostAsync(TestUsers.Rita, "requisitions", new
        {
            id = requisitionId,
            costCentreCode = world.CostCentre,
            supplierId = world.Supplier,
            justification = "System test purchase",
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3),
            lines = new[] { new { description = "Widget", quantity, unitOfMeasure = "EA", unitPrice } },
        }), patience);

        string requisition = $"requisitions/{requisitionId}";

        await Steps.OnceAsync(
            () => gateway.PostAsync(TestUsers.Rita, $"{requisition}/submit"),
            async () => (await gateway.GetAsync(TestUsers.Rita, requisition)).Text("status") != "Draft");

        JsonElement reserved = await Eventually.MatchesAsync(
            () => gateway.GetAsync(TestUsers.Rita, requisition),
            static r => r.Text("status") is not ("Draft" or "Submitted"),
            patience);

        reserved.Text("status").ShouldBe("PendingApproval", $"Budgets should have reserved {amount}: {reserved}");

        await Steps.OnceAsync(
            () => gateway.PostAsync(TestUsers.Mark, $"{requisition}/approve"),
            async () => (await gateway.GetAsync(TestUsers.Rita, requisition)).Text("status") != "PendingApproval");

        JsonElement? drafted = await Eventually.MatchesAsync(
            () => gateway.FindAsync(TestUsers.Bruno, $"purchase-orders/by-requisition/{requisitionId}"),
            static order => order is not null,
            patience);

        Guid orderId = drafted!.Value.Id();
        string order = $"purchase-orders/{orderId}";

        await Steps.OnceAsync(
            () => gateway.PostAsync(TestUsers.Bruno, $"{order}/issue"),
            async () => (await gateway.GetAsync(TestUsers.Bruno, order)).Text("status") != "Draft");

        JsonElement issued = await Eventually.MatchesAsync(
            () => gateway.GetAsync(TestUsers.Bruno, order),
            static o => o.Text("status") != "CommitmentPending",
            patience);

        issued.Text("status").ShouldBe("Issued", $"Budgets should have committed {amount}: {issued}");

        await gateway.PostAsync(TestUsers.Rosa, $"{order}/receipts", new
        {
            id = Guid.CreateVersion7(),
            lines = new[] { new { lineNumber = 1, quantity } },
        });

        // Dated so it falls due today, and a payment run drafted today takes it.
        Guid invoiceId = Guid.CreateVersion7();
        string invoiceNumber = $"SYS-{requisitionId.ToString("N")[^12..].ToUpperInvariant()}";

        await gateway.PostAsync(TestUsers.Alice, "invoices", new
        {
            id = invoiceId,
            supplierId = world.Supplier,
            supplierInvoiceNumber = invoiceNumber,
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-world.PaymentTermsDays),
            purchaseOrderId = orderId,
            lines = new[] { new { lineNumber = 1, quantity, unitPrice } },
            total = amount,
        });

        // Payables may hear of the order and the receipt after the invoice; it matches when they arrive.
        JsonElement invoice = await Eventually.MatchesAsync(
            () => gateway.GetAsync(TestUsers.Alice, $"invoices/{invoiceId}"),
            static i => i.Text("status") is not ("AwaitingPurchaseOrder" or "AwaitingReceipt"),
            patience);

        invoice.Text("status").ShouldBe("Payable", invoice.ToString());

        return new Purchase(requisitionId, orderId, invoiceId, invoiceNumber, amount);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"requisition {Requisition}, order {Order}, invoice {Invoice} for {Amount}");
}

/// <summary>A payment run drafted by tess and released by trevor, the second pair of eyes.</summary>
internal static class PaymentRun
{
    public static async Task<JsonElement> DraftAndReleaseAsync(Gateway gateway)
    {
        JsonElement draft = await gateway.PostAsync(TestUsers.Tess, "payment-runs", new
        {
            id = Guid.CreateVersion7(),
            executionDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        return await gateway.PostAsync(TestUsers.Trevor, $"payment-runs/{draft.Id()}/release");
    }
}

internal static class Steps
{
    /// <summary>
    /// Runs a command that may already have happened. When a retried request's first attempt landed, the service
    /// refuses the repeat because the state has moved on; that refusal is success if <paramref name="done"/> says
    /// the command took effect, and a real failure otherwise.
    /// </summary>
    public static async Task OnceAsync(Func<Task> command, Func<Task<bool>> done)
    {
        try
        {
            await command();
        }
        catch (ProblemException)
        {
            if (!await done())
            {
                throw;
            }
        }
    }

    /// <summary>Repeats a request while the service answers that something it needs has not reached it yet.</summary>
    public static async Task UntilKnownAsync(Func<Task> request, TimeSpan patience)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + patience;

        while (true)
        {
            try
            {
                await request();
                return;
            }
            catch (ProblemException problem) when (problem.Code?.EndsWith("_unknown", StringComparison.Ordinal) == true && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }
}
