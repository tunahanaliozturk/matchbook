using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.Payables.Api.Invoices;
using Matchbook.Payables.Application;
using Matchbook.Payables.Application.Invoices;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>Who may call what, what a malformed request gets, and what the published description promises.</summary>
public sealed class ApiSurfaceTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>
{
    private readonly Scenario _given = new(fixture);

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/invoices" },
        { "GET", "/invoices/exceptions" },
        { "GET", $"/invoices/{Guid.Empty}" },
        { "POST", "/invoices" },
        { "POST", $"/invoices/{Guid.Empty}/accept-price-variance" },
        { "POST", $"/invoices/{Guid.Empty}/clear-suspected-duplicate" },
        { "POST", "/payment-runs" },
        { "GET", $"/payment-runs/{Guid.Empty}" },
        { "POST", $"/payment-runs/{Guid.Empty}/release" },
        { "POST", $"/payment-runs/{Guid.Empty}/cancel" },
        { "GET", $"/payment-runs/{Guid.Empty}/file" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_refuses_a_caller_without_a_token(string method, string path) =>
        (await _given.Host.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_refuses_a_role_with_no_business_in_payables(string method, string path) =>
        (await _given.As(TestUsers.Rita).SendAsync(new HttpRequestMessage(new HttpMethod(method), path)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("POST", "/invoices", "tess")]
    [InlineData("POST", "/invoices", "aaron")]
    [InlineData("POST", "/invoices", "audrey")]
    [InlineData("POST", "/payment-runs", "alice")]
    [InlineData("POST", "/payment-runs", "audrey")]
    [InlineData("GET", "/payment-runs/00000000-0000-0000-0000-000000000000", "alice")]
    [InlineData("POST", "/invoices/00000000-0000-0000-0000-000000000000/accept-price-variance", "tess")]
    public async Task Each_action_is_refused_to_the_roles_it_is_not_for(string method, string path, string user)
    {
        Actor actor = user switch
        {
            "tess" => TestUsers.Tess,
            "aaron" => TestUsers.Aaron,
            "alice" => TestUsers.Alice,
            _ => TestUsers.Audrey,
        };

        (await _given.As(actor).SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_auditor_reads_invoices_and_runs_but_changes_nothing()
    {
        (await _given.As(TestUsers.Audrey).GetAsync(new Uri("/invoices", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await _given.As(TestUsers.Audrey).GetAsync(new Uri($"/payment-runs/{Guid.CreateVersion7()}", UriKind.Relative)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "payment_run.not_found");
    }

    [Fact]
    public async Task A_capture_missing_its_fields_is_a_400_naming_them()
    {
        HttpResponseMessage response = await _given.As(TestUsers.Alice).PostAsJsonAsync(
            new Uri("/invoices", UriKind.Relative),
            new { supplierInvoiceNumber = "INV-1" });

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        using JsonDocument problem = JsonDocument.Parse(body);
        JsonElement errors = problem.RootElement.GetProperty("errors");
        foreach (string field in (string[])["SupplierId", "InvoiceDate", "PurchaseOrderId", "Lines", "Total"])
        {
            errors.EnumerateObject().ShouldContain(error => error.Name.Equals(field, StringComparison.OrdinalIgnoreCase), body);
        }
    }

    [Fact]
    public async Task A_list_pages_by_cursor_newest_first_and_filters_by_status()
    {
        Guid supplier = await _given.SupplierAsync();
        Guid order = Guid.CreateVersion7();
        Guid[] captured = new Guid[3];
        for (int i = 0; i < captured.Length; i++)
        {
            captured[i] = (await _given.CaptureAsync(
                TestUsers.Alice,
                Scenario.Invoice(supplier, order, $"INV-PAGE-{i}", Scenario.Today, (1, 1, 10m + i)))).Id;
        }

        Page<InvoiceSummary> first = await ListAsync("status=AwaitingPurchaseOrder&limit=2");
        Page<InvoiceSummary> second = await ListAsync($"status=AwaitingPurchaseOrder&limit=2&after={first.Next}");

        first.Items.Select(invoice => invoice.Id).ShouldBe([captured[2], captured[1]]);
        second.Items.Select(invoice => invoice.Id).ShouldBe([captured[0]]);
        second.Next.ShouldBeNull();
    }

    [Fact]
    public async Task The_published_description_covers_every_endpoint_and_its_problems()
    {
        using JsonDocument openApi = JsonDocument.Parse(await _given.Host.CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));
        JsonElement paths = openApi.RootElement.GetProperty("paths");

        foreach ((string method, string path) in (ValueTuple<string, string>[])
        [
            ("post", "/invoices"), ("get", "/invoices"), ("get", "/invoices/exceptions"), ("get", "/invoices/{id}"),
            ("post", "/invoices/{id}/accept-price-variance"), ("post", "/invoices/{id}/clear-suspected-duplicate"),
            ("post", "/payment-runs"), ("get", "/payment-runs/{id}"), ("post", "/payment-runs/{id}/release"),
            ("post", "/payment-runs/{id}/cancel"), ("get", "/payment-runs/{id}/file"),
        ])
        {
            JsonElement operation = paths.GetProperty(path).GetProperty(method);
            JsonElement responses = operation.GetProperty("responses");
            responses.GetProperty("403").GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue($"{method} {path}");
        }

        paths.GetProperty("/invoices").GetProperty("post").GetProperty("requestBody").ToString().ShouldContain("CaptureInvoiceRequest");
        JsonElement file = paths.GetProperty("/payment-runs/{id}/file").GetProperty("get").GetProperty("responses");
        file.ToString().ShouldContain("application/xml", Case.Sensitive, file.ToString());
    }

    private async Task<Page<InvoiceSummary>> ListAsync(string query) =>
        (await _given.As(TestUsers.Alice).GetFromJsonAsync<Page<InvoiceSummary>>(new Uri($"/invoices?{query}", UriKind.Relative), Scenario.Json))!;
}
