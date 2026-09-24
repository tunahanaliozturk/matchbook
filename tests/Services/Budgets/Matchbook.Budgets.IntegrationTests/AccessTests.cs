using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.Budgets.Application.Budgets;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Budgets.IntegrationTests;

/// <summary>Who may call what, and what the published description of the API promises.</summary>
public sealed class AccessTests(BudgetsFixture fixture) : IClassFixture<BudgetsFixture>
{
    [Fact]
    public async Task A_request_without_a_token_is_unauthorized()
    {
        using HttpClient anonymous = fixture.Host.CreateClient();

        (await anonymous.GetAsync(new Uri("/cost-centres", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(new Uri($"/budgets?fiscalYear={BudgetsFixture.FiscalYear}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_a_budget_admin_changes_anything()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(1_000m);

        foreach (Actor someone in (Actor[])[TestUsers.Rita, TestUsers.Audrey, TestUsers.Fiona])
        {
            using HttpClient client = fixture.Host.ClientFor(someone);
            (await client.PostAsJsonAsync(new Uri("/cost-centres", UriKind.Relative), new { code = BudgetsFixture.NewCode(), name = "No", managerId = TestUsers.Mark.Id }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, someone.Name);
            (await client.PutAsJsonAsync(new Uri($"/cost-centres/{budget.CostCentreCode}", UriKind.Relative), new { version = 1, name = "No", managerId = TestUsers.Mark.Id, isActive = true }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, someone.Name);
            (await client.PostAsJsonAsync(new Uri($"/budgets/{budget.Id}/allotment-changes", UriKind.Relative), new { change = 1m }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, someone.Name);
        }

        (await fixture.BudgetAsync(budget.Id)).Allotted.ShouldBe(1_000m);
    }

    [Fact]
    public async Task The_auditor_and_approvers_read_budgets_and_a_requester_does_not()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(1_000m);
        var balance = new Uri($"/budgets/{budget.Id}", UriKind.Relative);

        foreach (Actor reader in (Actor[])[TestUsers.Audrey, TestUsers.Mark, TestUsers.Fiona, TestUsers.Carl])
        {
            using HttpClient client = fixture.Host.ClientFor(reader);
            (await client.GetAsync(balance)).StatusCode.ShouldBe(HttpStatusCode.OK, reader.Name);
        }

        using HttpClient rita = fixture.Host.ClientFor(TestUsers.Rita);
        (await rita.GetAsync(balance)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rita.GetAsync(new Uri($"/cost-centres/{budget.CostCentreCode}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_openapi_document_describes_every_endpoint_with_its_responses_and_problems()
    {
        using HttpClient anonymous = fixture.Host.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await anonymous.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));
        JsonElement paths = document.RootElement.GetProperty("paths");

        string[] expected =
        [
            "post /cost-centres", "get /cost-centres", "get /cost-centres/{code}", "put /cost-centres/{code}",
            "post /budgets", "get /budgets", "get /budgets/overspends", "get /budgets/{id}",
            "post /budgets/{id}/allotment-changes", "get /budgets/{id}/ledger",
        ];
        string[] described =
        [
            .. paths.EnumerateObject().SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put")
                .Select(operation => $"{operation.Name} {path.Name}")),
        ];
        described.ShouldBe(expected, ignoreOrder: true);

        foreach (JsonElement operation in paths.EnumerateObject().SelectMany(path => path.Value.EnumerateObject()).Select(o => o.Value))
        {
            string name = operation.GetProperty("operationId").GetString()!;
            JsonProperty[] all = [.. operation.GetProperty("responses").EnumerateObject()];

            all.Any(r => r.Name.StartsWith('2') && r.Value.TryGetProperty("content", out _)).ShouldBeTrue(name);
            all.Select(r => r.Name).ShouldContain("401", name);
            all.Select(r => r.Name).ShouldContain("403", name);
            all.Where(r => r.Name.StartsWith('4'))
                .All(r => r.Value.GetProperty("content").TryGetProperty("application/problem+json", out _))
                .ShouldBeTrue(name);
        }
    }
}
