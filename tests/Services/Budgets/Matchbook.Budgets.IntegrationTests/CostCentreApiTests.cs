using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Application.CostCentres;
using Matchbook.Contracts.Budgets;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.IntegrationTests;

public sealed class CostCentreApiTests(BudgetsFixture fixture) : IClassFixture<BudgetsFixture>
{
    private static readonly Uri CostCentres = new("/cost-centres", UriKind.Relative);

    private static Uri CostCentre(string code) => new($"/cost-centres/{code}", UriKind.Relative);

    [Fact]
    public async Task Creating_and_changing_a_cost_centre_publishes_it_with_a_rising_version()
    {
        CostCentreView created = await fixture.CreateCostCentreAsync();
        CostCentreView renamed = await ChangeAsync(created.Code, new { version = 1, name = "Renamed", managerId = TestUsers.Maya.Id, isActive = true });
        CostCentreView closed = await ChangeAsync(created.Code, new { version = 2, name = "Renamed", managerId = TestUsers.Maya.Id, isActive = false });

        (created.Version, renamed.Version, closed.Version).ShouldBe((1L, 2L, 3L));

        // Each save has its own outbox, and outboxes are delivered independently, so the three may arrive in any
        // order. That is why the version is on the message.
        CostCentreChanged[] published = await Eventually.MatchesAsync(
            () => Task.FromResult(fixture.Probe.Received<CostCentreChanged>().Where(c => c.CostCentreCode == created.Code).OrderBy(c => c.Version).ToArray()),
            static changes => changes.Length == 3);
        published.Select(c => c.Version).ShouldBe([1L, 2L, 3L]);
        (published[2].Name, published[2].ManagerId, published[2].IsActive).ShouldBe(("Renamed", TestUsers.Maya.Id, false));
        published.ShouldAllBe(c => c.OccurredAt.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_keeps_the_version()
    {
        CostCentreView created = await fixture.CreateCostCentreAsync();

        CostCentreView same = await ChangeAsync(
            created.Code, new { version = 1, name = created.Name, managerId = created.ManagerId, isActive = true });

        same.ShouldBe(created);
    }

    [Fact]
    public async Task A_change_against_an_older_version_is_a_conflict()
    {
        CostCentreView created = await fixture.CreateCostCentreAsync();
        await ChangeAsync(created.Code, new { version = 1, name = "First edit", managerId = created.ManagerId, isActive = true });

        HttpResponseMessage stale = await fixture.Bob.PutAsJsonAsync(
            CostCentre(created.Code), new { version = 1, name = "Second edit", managerId = created.ManagerId, isActive = true });

        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency.conflict");
    }

    [Fact]
    public async Task Repeating_a_create_with_the_same_id_returns_the_first_result_and_creates_nothing_more()
    {
        var request = new { id = Guid.NewGuid(), code = BudgetsFixture.NewCode(), name = "Idempotent", managerId = TestUsers.Mark.Id };
        HttpResponseMessage first = await fixture.Bob.PostAsJsonAsync(CostCentres, request);
        await ChangeAsync(request.code, new { version = 1, name = "Changed since", managerId = TestUsers.Mark.Id, isActive = true });

        HttpResponseMessage repeat = await fixture.Bob.PostAsJsonAsync(CostCentres, request);

        (first.StatusCode, repeat.StatusCode).ShouldBe((HttpStatusCode.Created, HttpStatusCode.Created));
        JsonElement.DeepEquals(await BodyAsync(first), await BodyAsync(repeat)).ShouldBeTrue();
        (await fixture.InDatabaseAsync(db => db.CostCentres.CountAsync(c => c.Code == request.code))).ShouldBe(1);
    }

    [Fact]
    public async Task Reusing_an_id_for_a_different_create_is_refused()
    {
        Guid id = Guid.NewGuid();
        (await fixture.Bob.PostAsJsonAsync(CostCentres, new { id, code = BudgetsFixture.NewCode(), name = "First", managerId = TestUsers.Mark.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage reused = await fixture.Bob.PostAsJsonAsync(
            CostCentres, new { id, code = BudgetsFixture.NewCode(), name = "Second", managerId = TestUsers.Mark.Id });

        await reused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }

    [Fact]
    public async Task A_second_cost_centre_with_the_same_code_is_a_conflict()
    {
        CostCentreView created = await fixture.CreateCostCentreAsync();

        HttpResponseMessage duplicate = await fixture.Bob.PostAsJsonAsync(
            CostCentres, new { code = created.Code, name = "Again", managerId = TestUsers.Mark.Id });

        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "cost_centre.already_exists");
    }

    [Fact]
    public async Task A_code_off_the_pattern_breaks_a_rule_and_a_missing_name_is_a_malformed_request()
    {
        await (await fixture.Bob.PostAsJsonAsync(CostCentres, new { code = "eng-platform", name = "Lower case", managerId = TestUsers.Mark.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "cost_centre.code_invalid");

        HttpResponseMessage missing = await fixture.Bob.PostAsJsonAsync(CostCentres, new { code = BudgetsFixture.NewCode(), managerId = TestUsers.Mark.Id });

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(missing)).GetProperty("errors").TryGetProperty("Name", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Cost_centres_list_in_code_order_a_page_at_a_time()
    {
        string[] codes = [.. (await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => fixture.CreateCostCentreAsync()))).Select(c => c.Code).Order(StringComparer.Ordinal)];
        using HttpClient rita = fixture.Host.ClientFor(TestUsers.Rita);

        var seen = new List<string>();
        string? after = null;
        do
        {
            string query = after is null ? "?limit=2" : $"?limit=2&after={after}";
            var page = (await rita.GetFromJsonAsync<Page<CostCentreView>>(new Uri($"/cost-centres{query}", UriKind.Relative)))!;
            page.Items.Count.ShouldBeLessThanOrEqualTo(2);
            seen.AddRange(page.Items.Select(c => c.Code));
            after = page.Next;
        }
        while (after is not null);

        seen.ShouldBe([.. seen.Order(StringComparer.Ordinal)]);
        seen.ShouldBeUnique();
        codes.ShouldBeSubsetOf(seen);
    }

    [Fact]
    public async Task An_unknown_cost_centre_is_not_found() =>
        await (await fixture.Bob.GetAsync(CostCentre("NOPE-NOPE"))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "cost_centre.not_found");

    private async Task<CostCentreView> ChangeAsync(string code, object change)
    {
        HttpResponseMessage response = await fixture.Bob.PutAsJsonAsync(CostCentre(code), change);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CostCentreView>())!;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
