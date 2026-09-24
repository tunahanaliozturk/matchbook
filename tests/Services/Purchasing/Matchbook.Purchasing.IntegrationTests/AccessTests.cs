using System.Net;
using System.Text.Json;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

public sealed class AccessTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    [Fact]
    public async Task A_request_without_a_token_is_a_401()
    {
        HttpClient anonymous = fixture.Host.CreateClient();

        (await anonymous.GetAsync(Routes.Orders())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(Routes.Issue(Guid.CreateVersion7()), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_a_buyer_amends_issues_and_closes_and_only_a_receiver_records_receipts()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (2m, 5m));
        HttpClient rosa = fixture.ClientFor(TestUsers.Rosa);
        HttpClient bruno = fixture.ClientFor(TestUsers.Bruno);

        (await rosa.PutJsonAsync(Routes.Line(draft.Id, 1), new { quantity = 1m, unitPrice = 5m })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rosa.PostAsync(Routes.Issue(draft.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rosa.PostAsync(Routes.Cancel(draft.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rosa.PostAsync(Routes.ShortClose(draft.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bruno.PostJsonAsync(Routes.Receipts(draft.Id), new { lines = new[] { new { lineNumber = 1, quantity = 1m } } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await fixture.GetAsync(draft.Id)).Status.ShouldBe(Domain.PurchaseOrderStatus.Draft);
    }

    [Fact]
    public async Task An_auditor_reads_everything_and_changes_nothing()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (2m, 5m));
        HttpClient audrey = fixture.ClientFor(TestUsers.Audrey);

        (await audrey.GetAsync(Routes.Orders())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await audrey.GetAsync(Routes.Order(draft.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await audrey.GetAsync(Routes.Receipts(draft.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await audrey.PostAsync(Routes.Cancel(draft.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await audrey.PutJsonAsync(Routes.Line(draft.Id, 1), new { quantity = 1m, unitPrice = 5m })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_with_no_purchasing_role_cannot_even_read()
    {
        HttpClient requester = fixture.ClientFor(TestUsers.Stranger(Roles.Requester));

        (await requester.GetAsync(Routes.Orders())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_missing_amendment_value_is_a_400_and_not_a_line_set_to_zero()
    {
        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (2m, 5m));

        HttpResponseMessage response = await fixture.ClientFor(TestUsers.Bruno).PutJsonAsync(Routes.Line(draft.Id, 1), new { unitPrice = 5m });

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        JsonDocument.Parse(body).RootElement.GetProperty("errors").EnumerateObject()
            .ShouldContain(static field => field.Name.EndsWith("Quantity", StringComparison.OrdinalIgnoreCase));
        (await fixture.GetAsync(draft.Id)).Lines[0].Quantity.ShouldBe(2m);
    }

    [Fact]
    public async Task The_openapi_document_describes_every_endpoint_with_its_body_success_and_problem_responses()
    {
        using JsonDocument document = JsonDocument.Parse(await fixture.Host.CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));
        JsonElement paths = document.RootElement.GetProperty("paths");

        List<string> operations = [];

        foreach (JsonProperty path in paths.EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject().Where(static member => member.Name is "get" or "put" or "post"))
            {
                string name = $"{operation.Name.ToUpperInvariant()} {path.Name}";
                operations.Add(name);
                JsonElement responses = operation.Value.GetProperty("responses");

                JsonProperty success = responses.EnumerateObject().Single(static response => response.Name.StartsWith('2'));
                success.Value.GetProperty("content").GetProperty("application/json").GetProperty("schema");

                foreach (string status in (string[])["401", "403"])
                {
                    responses.GetProperty(status).GetProperty("content").GetProperty("application/problem+json");
                }

                if (operation.Name is "put" || path.Name.EndsWith("/receipts", StringComparison.Ordinal) && operation.Name == "post")
                {
                    operation.Value.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
                    responses.GetProperty("400").GetProperty("content").GetProperty("application/problem+json");
                }
            }
        }

        operations.Count.ShouldBe(10, string.Join(Environment.NewLine, operations));
    }
}
