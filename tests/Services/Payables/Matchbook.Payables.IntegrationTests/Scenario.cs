using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Suppliers;
using Matchbook.Payables.Api.Invoices;
using Matchbook.Payables.Api.PaymentRuns;
using Matchbook.Payables.Application;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Application.PaymentRuns;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// The other services' side of a scenario (suppliers, orders, receipts, sent as the events they would publish) and
/// the Payables calls a test makes, so each test reads as the story it tells.
/// </summary>
internal sealed class Scenario(PayablesFixture fixture)
{
    /// <summary>The key ServiceHost configures (<c>test</c>, 32 zero bytes), so events carry IBANs the host can read.</summary>
    public static readonly ColumnProtector Protector = new(new Dictionary<string, byte[]> { ["test"] = new byte[32] }, "test");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public ServiceHost<Program> Host => fixture.Host;

    public EventProbe Probe => fixture.Probe;

    public HttpClient As(Actor actor) => fixture.Host.ClientFor(actor);

    /// <summary>Publishes the supplier as Suppliers would, and waits until Payables holds that version.</summary>
    public async Task<Guid> SupplierAsync(
        string iban = "GB82WEST12345698765432",
        int paymentTermsDays = 30,
        Guid? id = null,
        long version = 1,
        int accountVersion = 1,
        bool active = true)
    {
        Guid supplierId = id ?? Guid.CreateVersion7();
        await Probe.PublishAsync(SupplierChanged(supplierId, iban, paymentTermsDays, version, accountVersion, active));
        await UntilAsync(db => db.Suppliers.AnyAsync(supplier => supplier.Id == supplierId && supplier.Version >= version));
        return supplierId;
    }

    public static SupplierChanged SupplierChanged(
        Guid supplierId,
        string iban,
        int paymentTermsDays = 30,
        long version = 1,
        int accountVersion = 1,
        bool active = true) =>
        new(
            supplierId,
            version,
            "ACME Industrial Supplies GmbH",
            "DE",
            active ? SupplierStatus.Active : SupplierStatus.Blocked,
            paymentTermsDays,
            new VerifiedBankAccount(accountVersion, Protector.Protect(iban), iban[^4..], "NWBKGB2L", "ACME Industrial & Supplies"),
            DateTimeOffset.UtcNow);

    /// <summary>Issues an order as Purchasing would, and waits until Payables has it.</summary>
    public async Task<Guid> OrderAsync(Guid supplierId, params (int Line, decimal Quantity, decimal UnitPrice)[] lines)
    {
        PurchaseOrderIssued issued = OrderIssued(Guid.CreateVersion7(), supplierId, lines);
        await Probe.PublishAsync(issued);
        await OrderArrivedAsync(issued.PurchaseOrderId);
        return issued.PurchaseOrderId;
    }

    public static PurchaseOrderIssued OrderIssued(Guid orderId, Guid supplierId, params (int Line, decimal Quantity, decimal UnitPrice)[] lines) =>
        new(
            orderId,
            $"PO-2026-{orderId.GetHashCode() & 0xFFFFF:D6}",
            Guid.CreateVersion7(),
            supplierId,
            "ENG-PLATFORM",
            2026,
            [.. lines.Select(line => new PurchaseOrderLine(
                line.Line,
                $"Item {line.Line}",
                line.Quantity,
                "ea",
                line.UnitPrice,
                Amounts.Line(line.Quantity, line.UnitPrice)))],
            lines.Sum(line => Amounts.Line(line.Quantity, line.UnitPrice)),
            TestUsers.Bruno.Id,
            DateTimeOffset.UtcNow);

    public Task OrderArrivedAsync(Guid orderId) =>
        UntilAsync(db => db.PurchaseOrders.AnyAsync(order => order.Id == orderId && order.IssuedAt != null));

    /// <summary>Records a receipt as Purchasing would, and waits until Payables has stored it.</summary>
    public async Task<Guid> ReceiveAsync(Guid orderId, params (int Line, decimal Quantity)[] lines)
    {
        GoodsReceived received = Receipt(orderId, lines);
        await Probe.PublishAsync(received);
        await UntilAsync(db => db.Receipts.AnyAsync(receipt => receipt.Id == received.ReceiptId));
        return received.ReceiptId;
    }

    public static GoodsReceived Receipt(Guid orderId, params (int Line, decimal Quantity)[] lines) =>
        new(
            Guid.CreateVersion7(),
            orderId,
            [.. lines.Select(line => new ReceiptLine(line.Line, line.Quantity))],
            TestUsers.Rosa.Id,
            DateTimeOffset.UtcNow);

    public static CaptureInvoiceRequest Invoice(
        Guid supplierId,
        Guid orderId,
        string number,
        DateOnly invoiceDate,
        params (int Line, decimal Quantity, decimal UnitPrice)[] lines) =>
        new(
            null,
            supplierId,
            number,
            invoiceDate,
            orderId,
            [.. lines.Select(line => new CaptureInvoiceLineRequest(line.Line, line.Quantity, line.UnitPrice))],
            lines.Sum(line => Amounts.Line(line.Quantity, line.UnitPrice)));

    public Task<HttpResponseMessage> PostCaptureAsync(Actor clerk, CaptureInvoiceRequest request) =>
        As(clerk).PostAsJsonAsync(new Uri("/invoices", UriKind.Relative), request, Json);

    public async Task<InvoiceView> CaptureAsync(Actor clerk, CaptureInvoiceRequest request)
    {
        HttpResponseMessage response = await PostCaptureAsync(clerk, request);
        return await ReadAsync<InvoiceView>(response, HttpStatusCode.Created);
    }

    /// <summary>A payable invoice for <paramref name="supplierId"/>, due well before today, on an order of its own.</summary>
    public async Task<InvoiceView> PayableInvoiceAsync(Guid supplierId, decimal amount, string? number = null)
    {
        Guid orderId = await OrderAsync(supplierId, (1, 1, amount));
        await ReceiveAsync(orderId, (1, 1));
        InvoiceView invoice = await CaptureAsync(
            TestUsers.Alice,
            Invoice(supplierId, orderId, number ?? $"INV-{Guid.NewGuid():N}"[..20], Today.AddDays(-40), (1, 1, amount)));
        invoice.Status.ShouldBe(InvoiceStatus.Payable);
        return invoice;
    }

    public async Task<InvoiceView> InvoiceAsync(Guid invoiceId) =>
        (await As(TestUsers.Audrey).GetFromJsonAsync<InvoiceView>(new Uri($"/invoices/{invoiceId}", UriKind.Relative), Json))!;

    public Task<InvoiceView> InvoiceInAsync(Guid invoiceId, InvoiceStatus status) =>
        Eventually.MatchesAsync(() => InvoiceAsync(invoiceId), invoice => invoice.Status == status);

    public Task<HttpResponseMessage> PostDraftAsync(Actor treasurer, DateOnly executionDate, Guid? id = null) =>
        As(treasurer).PostAsJsonAsync(new Uri("/payment-runs", UriKind.Relative), new DraftPaymentRunRequest(id, executionDate), Json);

    public async Task<PaymentRunView> DraftAsync(Actor treasurer, Guid? id = null) =>
        await ReadAsync<PaymentRunView>(await PostDraftAsync(treasurer, Today, id), HttpStatusCode.Created);

    public Task<HttpResponseMessage> PostReleaseAsync(Actor treasurer, Guid runId) =>
        As(treasurer).PostAsync(new Uri($"/payment-runs/{runId}/release", UriKind.Relative), null);

    public async Task<PaymentRunView> ReleaseAsync(Actor treasurer, Guid runId) =>
        await ReadAsync<PaymentRunView>(await PostReleaseAsync(treasurer, runId), HttpStatusCode.OK);

    public async Task<T> DbAsync<T>(Func<IPayablesDb, Task<T>> query) =>
        await Host.InScopeAsync(services => query(services.GetRequiredService<IPayablesDb>()));

    public Task UntilAsync(Func<IPayablesDb, Task<bool>> condition) =>
        Eventually.MatchesAsync(() => DbAsync(condition), static met => met);

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
}
