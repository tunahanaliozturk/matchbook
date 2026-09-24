using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Requisitions;
using Matchbook.Contracts.Suppliers;
using Matchbook.Requisitions.Api.Features.Requisitions;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Application.Features.Requisitions;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.CreateRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListCostCentreOptions;
using Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListSupplierOptions;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>The HTTP surface: idempotent creates, who may call what, what a response carries, and the document.</summary>
public sealed class RequisitionApiTests(RequisitionsFixture fixture) : IClassFixture<RequisitionsFixture>
{
    [Fact]
    public async Task A_create_sent_again_with_its_id_returns_the_first_requisition_instead_of_a_second()
    {
        CreateRequisitionRequest request = Scenario.Request(2_500m, id: Guid.CreateVersion7());

        HttpResponseMessage first = await fixture.PostCreateAsync(TestUsers.Rita, request);
        HttpResponseMessage again = await fixture.PostCreateAsync(TestUsers.Rita, request);

        RequisitionView created = await first.ReadAsync<RequisitionView>(HttpStatusCode.Created);
        RequisitionView repeated = await again.ReadAsync<RequisitionView>(HttpStatusCode.Created);
        created.Id.ShouldBe(request.Id!.Value);
        repeated.Number.ShouldBe(created.Number);
        again.Headers.Location.ShouldBe(first.Headers.Location);
        (await MineAsync(TestUsers.Rita)).Count(item => item.Id == created.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Two_creates_racing_with_one_id_both_get_the_one_requisition()
    {
        CreateRequisitionRequest request = Scenario.Request(2_500m, id: Guid.CreateVersion7());

        HttpResponseMessage[] answers = await Task.WhenAll(
            fixture.PostCreateAsync(TestUsers.Rita, request),
            fixture.PostCreateAsync(TestUsers.Rita, request));

        RequisitionView[] views = await Task.WhenAll(answers.Select(static answer => answer.ReadAsync<RequisitionView>(HttpStatusCode.Created)));
        views.Select(static view => view.Number).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_id_reused_for_different_content_or_by_someone_else_is_refused()
    {
        CreateRequisitionRequest request = Scenario.Request(2_500m, id: Guid.CreateVersion7());
        await (await fixture.PostCreateAsync(TestUsers.Rita, request)).ReadAsync<RequisitionView>(HttpStatusCode.Created);

        await (await fixture.PostCreateAsync(TestUsers.Rita, request with { Justification = "Something else" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, CreateRequisitionHandler.IdReused);
        await (await fixture.PostCreateAsync(TestUsers.Stranger(Roles.Requester), request))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, CreateRequisitionHandler.IdReused);
    }

    [Fact]
    public async Task A_create_answers_with_the_new_state_and_where_to_find_it()
    {
        HttpResponseMessage response = await fixture.PostCreateAsync(TestUsers.Rita, Scenario.Request(2_500m));

        RequisitionView created = await response.ReadAsync<RequisitionView>(HttpStatusCode.Created);
        response.Headers.Location.ShouldBe(new Uri($"/requisitions/{created.Id}", UriKind.Relative));
        created.Status.ShouldBe(RequisitionStatus.Draft);
        created.Number.ShouldStartWith($"REQ-{DateTime.UtcNow.Year}-");
        created.Amount.ShouldBe(2_500m);
        created.Timeline.ShouldHaveSingleItem().ActorName.ShouldBe("rita");
    }

    [Fact]
    public async Task A_request_missing_a_field_is_a_400_naming_it()
    {
        HttpResponseMessage response = await fixture.Client(TestUsers.Rita).PostAsJsonAsync(
            Scenario.Url("/requisitions"),
            new { supplierId = RequisitionsFixture.SupplierId, justification = "No cost centre", lines = Array.Empty<object>() },
            Scenario.Json);

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain("CostCentreCode", Case.Insensitive);
    }

    [Fact]
    public async Task A_request_that_breaks_a_rule_is_a_422_with_its_code()
    {
        CreateRequisitionRequest tooMany = Scenario.Request(2_500m) with
        {
            Lines = [.. Enumerable.Repeat(new LineRequest("Pen", 1m, "EA", 1m), Requisition.MaxLines + 1)],
        };

        await (await fixture.PostCreateAsync(TestUsers.Rita, tooMany))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, RequisitionCodes.LineCountInvalid);
    }

    [Fact]
    public async Task A_draft_is_edited_and_cancelled_without_troubling_budgets()
    {
        RequisitionView draft = await fixture.CreateAsync(TestUsers.Rita, 2_500m);
        EditRequisitionRequest edit = new(
            RequisitionsFixture.Platform,
            RequisitionsFixture.SupplierId,
            "One laptop is enough",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            [new LineRequest("Laptop", 1m, "EA", 1_450m)]);

        RequisitionView edited = await (await fixture.Client(TestUsers.Rita).PutAsJsonAsync(Scenario.Url($"/requisitions/{draft.Id}"), edit, Scenario.Json))
            .ReadAsync<RequisitionView>();
        RequisitionView cancelled = await (await fixture.PostAsync(TestUsers.Rita, draft.Id, "cancel")).ReadAsync<RequisitionView>();

        edited.Amount.ShouldBe(1_450m);
        edited.Lines.ShouldHaveSingleItem();
        cancelled.Status.ShouldBe(RequisitionStatus.Cancelled);
        await fixture.Probe.ShouldNotReceiveAsync<RequisitionCancelled>(message => message.RequisitionId == draft.Id, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_tells_budgets_to_release_the_funds()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita);
        Uri reject = Scenario.Url($"/requisitions/{pending.Id}/reject");

        (await fixture.Client(TestUsers.Mark).PostAsJsonAsync(reject, new { reason = "" }, Scenario.Json))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        RequisitionView rejected = await (await fixture.Client(TestUsers.Mark).PostAsJsonAsync(reject, new RejectRequisitionRequest("Use the spare laptops"), Scenario.Json))
            .ReadAsync<RequisitionView>();

        rejected.Status.ShouldBe(RequisitionStatus.Rejected);
        RequisitionRejected published = await fixture.Probe.WaitForAsync<RequisitionRejected>(message => message.RequisitionId == pending.Id);
        published.RejectedBy.ShouldBe(TestUsers.Mark.Id);
        published.Reason.ShouldBe("Use the spare laptops");
    }

    [Fact]
    public async Task Every_endpoint_turns_away_a_caller_without_a_token()
    {
        using HttpClient anonymous = fixture.Host.CreateClient();
        Guid id = Guid.CreateVersion7();

        (await anonymous.GetAsync(Scenario.Url("/requisitions"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Scenario.Url($"/requisitions/{id}"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Scenario.Url("/requisitions"), Scenario.Request(100m), Scenario.Json)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(Scenario.Url($"/requisitions/{id}/approve"), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Scenario.Url("/approvals"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Scenario.Url("/requisitions/cost-centres"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Scenario.Url("/requisitions/suppliers"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Roles_open_only_their_own_doors()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita);

        (await fixture.Client(TestUsers.Bruno).GetAsync(Scenario.Url("/requisitions"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostCreateAsync(TestUsers.Mark, Scenario.Request(100m))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync(TestUsers.Rita, pending.Id, "approve")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync(TestUsers.Mark, pending.Id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_auditor_reads_every_requisition_and_changes_none()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita);

        (await fixture.GetAsync(TestUsers.Audrey, pending.Id)).Id.ShouldBe(pending.Id);
        (await MineAsync(TestUsers.Audrey)).ShouldContain(item => item.Id == pending.Id);
        (await fixture.PostAsync(TestUsers.Audrey, pending.Id, "approve")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync(TestUsers.Audrey, pending.Id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostCreateAsync(TestUsers.Audrey, Scenario.Request(100m))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_requester_cannot_read_someone_elses_requisition()
    {
        Actor otherRequester = TestUsers.Stranger(Roles.Requester);
        RequisitionView ritas = await fixture.CreateAsync(TestUsers.Rita, 2_500m);

        await (await fixture.Client(otherRequester).GetAsync(Scenario.Url($"/requisitions/{ritas.Id}")))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, RequisitionCodes.NotFound);
        (await MineAsync(otherRequester)).ShouldNotContain(item => item.Id == ritas.Id);
    }

    [Fact]
    public async Task A_requesters_list_pages_from_the_newest()
    {
        Actor requester = TestUsers.Stranger(Roles.Requester);
        RequisitionView first = await fixture.CreateAsync(requester, 1_000m);
        RequisitionView second = await fixture.CreateAsync(requester, 1_000m);
        RequisitionView third = await fixture.CreateAsync(requester, 1_000m);

        Page<RequisitionSummary> page = await ListAsync(requester, "?limit=2");
        Page<RequisitionSummary> next = await ListAsync(requester, $"?limit=2&after={page.NextCursor}");

        page.Items.Select(static item => item.Id).ShouldBe([third.Id, second.Id]);
        next.Items.Select(static item => item.Id).ShouldBe([first.Id]);
        next.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task The_form_offers_the_active_cost_centres_the_requester_does_not_manage()
    {
        Actor requester = TestUsers.Stranger(Roles.Requester);
        string managed = await fixture.CostCentreManagedByAsync(requester.Id);
        string inactive = await fixture.CostCentreManagedByAsync(TestUsers.Mark.Id);
        await fixture.DeliverAsync(new CostCentreChanged(inactive, 2, "Closed down", TestUsers.Mark.Id, false, DateTimeOffset.UtcNow));

        List<CostCentreOption> offered = await (await fixture.Client(requester).GetAsync(Scenario.Url("/requisitions/cost-centres")))
            .ReadAsync<List<CostCentreOption>>();

        offered.ShouldContain(new CostCentreOption(RequisitionsFixture.Platform, "Platform engineering"));
        offered.ShouldNotContain(option => option.Code == managed);
        offered.ShouldNotContain(option => option.Code == inactive);
        (await fixture.Client(TestUsers.Mark).GetAsync(Scenario.Url("/requisitions/cost-centres"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_form_offers_the_active_suppliers()
    {
        Guid blocked = Guid.CreateVersion7();
        await fixture.DeliverAsync(new SupplierChanged(blocked, 1, "Blocked Trading Ltd", "GB", SupplierStatus.Blocked, 30, null, DateTimeOffset.UtcNow));

        List<SupplierOption> offered = await (await fixture.Client(TestUsers.Rita).GetAsync(Scenario.Url("/requisitions/suppliers")))
            .ReadAsync<List<SupplierOption>>();

        offered.ShouldContain(new SupplierOption(RequisitionsFixture.SupplierId, "Acme Office Supplies BV"));
        offered.ShouldNotContain(option => option.Id == blocked);
        (await fixture.Client(TestUsers.Audrey).GetAsync(Scenario.Url("/requisitions/suppliers"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_openapi_document_describes_every_endpoint_with_its_bodies_and_problems()
    {
        using HttpClient anonymous = fixture.Host.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await anonymous.GetStringAsync(Scenario.Url("/openapi/v1.json")));
        JsonElement paths = document.RootElement.GetProperty("paths");

        (string Path, string Method, string Success, bool HasBody)[] operations =
        [
            ("/requisitions", "post", "201", true),
            ("/requisitions", "get", "200", false),
            ("/requisitions/cost-centres", "get", "200", false),
            ("/requisitions/suppliers", "get", "200", false),
            ("/requisitions/{id}", "get", "200", false),
            ("/requisitions/{id}", "put", "200", true),
            ("/requisitions/{id}/submit", "post", "200", false),
            ("/requisitions/{id}/cancel", "post", "200", false),
            ("/requisitions/{id}/approve", "post", "200", false),
            ("/requisitions/{id}/reject", "post", "200", true),
            ("/approvals", "get", "200", false),
        ];

        foreach ((string path, string method, string success, bool hasBody) in operations)
        {
            JsonElement operation = paths.GetProperty(path).GetProperty(method);
            JsonElement responses = operation.GetProperty("responses");

            responses.GetProperty(success).GetProperty("content").TryGetProperty("application/json", out _)
                .ShouldBeTrue($"{method} {path} documents its {success} body");
            foreach (string problem in (string[])["401", "403"])
            {
                responses.GetProperty(problem).GetProperty("content").TryGetProperty("application/problem+json", out _)
                    .ShouldBeTrue($"{method} {path} documents {problem} as a problem");
            }

            operation.TryGetProperty("requestBody", out _).ShouldBe(hasBody, $"{method} {path} request body");
        }
    }

    private async Task<Page<RequisitionSummary>> ListAsync(Actor actor, string query = "?limit=200") =>
        await (await fixture.Client(actor).GetAsync(Scenario.Url($"/requisitions{query}"))).ReadAsync<Page<RequisitionSummary>>();

    private async Task<IReadOnlyList<RequisitionSummary>> MineAsync(Actor actor) => (await ListAsync(actor)).Items;
}
