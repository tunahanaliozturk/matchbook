using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Application.Budgets;
using Matchbook.Budgets.Application.CostCentres;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.IntegrationTests;

public sealed class BudgetApiTests(BudgetsFixture fixture) : IClassFixture<BudgetsFixture>
{
    private static readonly Uri Budgets = new("/budgets", UriKind.Relative);

    private static Uri AllotmentChanges(Guid budgetId) => new($"/budgets/{budgetId}/allotment-changes", UriKind.Relative);

    [Fact]
    public async Task The_allotment_can_be_lowered_to_what_is_consumed_and_no_further()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(10_000m);
        await fixture.ReserveAsync(budget, 6_000m);

        AllotmentChangeView lowered = await ChangeAsync(budget.Id, new { change = -4_000m });
        lowered.Budget.ShouldBe(budget with { Allotted = 6_000m, Reserved = 6_000m, Available = 0m });

        await (await fixture.Bob.PostAsJsonAsync(AllotmentChanges(budget.Id), new { change = -0.01m }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "budget.allotment_below_consumed");
        await (await fixture.Bob.PostAsJsonAsync(AllotmentChanges(budget.Id), new { change = 0m }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "budget.allotment_change_zero");

        AllotmentChangeView raised = await ChangeAsync(budget.Id, new { change = 1_000m });
        (raised.Budget.Allotted, raised.Budget.Available).ShouldBe((7_000m, 1_000m));
        await fixture.FiguresShouldEqualTheLedgerAsync(budget.Id);
    }

    [Fact]
    public async Task Repeating_an_allotment_change_with_the_same_id_applies_it_once_and_returns_the_first_result()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(10_000m);
        var request = new { id = Guid.NewGuid(), change = 2_500m };

        HttpResponseMessage first = await fixture.Bob.PostAsJsonAsync(AllotmentChanges(budget.Id), request);
        await fixture.ReserveAsync(budget, 100m);
        HttpResponseMessage repeat = await fixture.Bob.PostAsJsonAsync(AllotmentChanges(budget.Id), request);

        (first.StatusCode, repeat.StatusCode).ShouldBe((HttpStatusCode.Created, HttpStatusCode.Created));
        JsonElement.DeepEquals(await BodyAsync(first), await BodyAsync(repeat)).ShouldBeTrue();
        (await fixture.BudgetAsync(budget.Id)).Allotted.ShouldBe(12_500m);
        (await fixture.LedgerAsync(budget.Id)).Count(e => e.DocumentId == request.id).ShouldBe(1);

        await (await fixture.Bob.PostAsJsonAsync(AllotmentChanges(budget.Id), request with { change = 2_600m }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }

    [Fact]
    public async Task Repeating_a_budget_opening_with_the_same_id_returns_the_first_budget()
    {
        CostCentreView costCentre = await fixture.CreateCostCentreAsync();
        var request = new { id = Guid.NewGuid(), costCentreCode = costCentre.Code, fiscalYear = BudgetsFixture.FiscalYear, allotted = 8_000m };

        HttpResponseMessage first = await fixture.Bob.PostAsJsonAsync(Budgets, request);
        HttpResponseMessage repeat = await fixture.Bob.PostAsJsonAsync(Budgets, request);

        (first.StatusCode, repeat.StatusCode).ShouldBe((HttpStatusCode.Created, HttpStatusCode.Created));
        first.Headers.Location.ShouldBe(new Uri($"/budgets/{request.id}", UriKind.Relative));
        JsonElement.DeepEquals(await BodyAsync(first), await BodyAsync(repeat)).ShouldBeTrue();
        (await fixture.InDatabaseAsync(db => db.Budgets.CountAsync(b => b.CostCentreCode == costCentre.Code))).ShouldBe(1);

        await (await fixture.Bob.PostAsJsonAsync(Budgets, request with { id = Guid.NewGuid() }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "budget.already_exists");
    }

    [Fact]
    public async Task A_budget_for_an_unknown_cost_centre_or_year_out_of_range_is_refused()
    {
        await (await fixture.Bob.PostAsJsonAsync(Budgets, new { costCentreCode = "NOPE-NOPE", fiscalYear = 2026, allotted = 1m }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "cost_centre.not_found");
        (await fixture.Bob.PostAsJsonAsync(Budgets, new { costCentreCode = "NOPE-NOPE", fiscalYear = 1999, allotted = 1m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await (await fixture.Bob.GetAsync(new Uri($"/budgets/{Guid.NewGuid()}", UriKind.Relative)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "budget.not_found");
    }

    [Fact]
    public async Task The_ledger_pages_in_the_order_it_was_written_and_the_last_page_has_no_cursor()
    {
        BudgetView budget = await fixture.OpenBudgetAsync(1_000m);
        foreach (decimal change in (decimal[])[100m, 200m, 300m, 400m])
        {
            await ChangeAsync(budget.Id, new { change });
        }

        var entries = new List<LedgerEntryView>();
        var pages = 0;
        string? after = null;
        do
        {
            string query = after is null ? "?limit=2" : $"?limit=2&after={after}";
            var page = (await fixture.Bob.GetFromJsonAsync<Page<LedgerEntryView>>(new Uri($"/budgets/{budget.Id}/ledger{query}", UriKind.Relative)))!;
            entries.AddRange(page.Items);
            after = page.Next;
            pages++;
        }
        while (after is not null);

        pages.ShouldBe(3);
        entries.Select(e => e.Step).ShouldBe(["Open", "Allot", "Allot", "Allot", "Allot"]);
        entries.Select(e => e.Allotted).ShouldBe([1_000m, 100m, 200m, 300m, 400m]);
        entries.Select(e => e.Sequence).ShouldBeInOrder(SortDirection.Ascending);
        entries.ShouldAllBe(e => e.ActorId == TestUsers.Bob.Id);
    }

    [Fact]
    public async Task An_invoice_beyond_its_commitment_is_reported_as_an_overspend_and_a_healthy_budget_is_not()
    {
        BudgetView overspent = await fixture.OpenBudgetAsync(1_000m);
        Guid order = await fixture.CommitAsync(overspent, await fixture.ReserveAsync(overspent, 1_000m), 1_000m);
        BudgetView healthy = await fixture.OpenBudgetAsync(1_000m);

        await fixture.Probe.PublishAsync(BudgetsFixture.Invoiced(Guid.CreateVersion7(), order, 1_200m));
        await Eventually.MatchesAsync(() => fixture.BudgetAsync(overspent.Id), static b => b.Actual == 1_200m);

        var report = (await fixture.Bob.GetFromJsonAsync<Page<BudgetView>>(
            new Uri($"/budgets/overspends?fiscalYear={BudgetsFixture.FiscalYear}", UriKind.Relative)))!;
        BudgetView listed = report.Items.ShouldHaveSingleItem();
        listed.ShouldBe(overspent with { Committed = 0m, Actual = 1_200m, Available = -200m, Overspend = 200m });
        report.Items.ShouldNotContain(b => b.Id == healthy.Id);
    }

    [Fact]
    public async Task Budgets_list_by_fiscal_year_in_cost_centre_order()
    {
        BudgetView[] opened = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => fixture.OpenBudgetAsync(100m)));

        var page = (await fixture.Bob.GetFromJsonAsync<Page<BudgetView>>(
            new Uri($"/budgets?fiscalYear={BudgetsFixture.FiscalYear}&limit=200", UriKind.Relative)))!;

        page.Items.Select(b => b.CostCentreCode).ShouldBe([.. page.Items.Select(b => b.CostCentreCode).Order(StringComparer.Ordinal)]);
        opened.Select(b => b.Id).ShouldBeSubsetOf(page.Items.Select(b => b.Id));
        (await fixture.Bob.GetAsync(new Uri("/budgets", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<AllotmentChangeView> ChangeAsync(Guid budgetId, object change)
    {
        HttpResponseMessage response = await fixture.Bob.PostAsJsonAsync(AllotmentChanges(budgetId), change);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AllotmentChangeView>())!;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
