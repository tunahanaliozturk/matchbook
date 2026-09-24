using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Api;
using Matchbook.Requisitions.Application.UseCases;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>The steps tests are written in: a requester raises, Budgets reserves, approvers decide.</summary>
internal static class Scenario
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>Two lines coming to <paramref name="amount"/> exactly: one priced to fit, and a fixed 100.00.</summary>
    public static CreateRequisitionRequest Request(decimal amount, string costCentre = RequisitionsFixture.Platform, Guid? id = null) =>
        new(
            id,
            costCentre,
            RequisitionsFixture.SupplierId,
            "Laptops for the two new platform engineers",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            [new LineRequest("Laptop", 1m, "EA", amount - 100m), new LineRequest("Laptop bag", 4m, "EA", 25m)]);

    public static Task<HttpResponseMessage> PostCreateAsync(this RequisitionsFixture fixture, Actor requester, CreateRequisitionRequest request) =>
        fixture.Client(requester).PostAsJsonAsync(Url("/requisitions"), request, Json);

    public static async Task<RequisitionView> CreateAsync(
        this RequisitionsFixture fixture,
        Actor requester,
        decimal amount = 3_000m,
        string costCentre = RequisitionsFixture.Platform) =>
        await (await fixture.PostCreateAsync(requester, Request(amount, costCentre))).ReadAsync<RequisitionView>(HttpStatusCode.Created);

    public static Task<HttpResponseMessage> PostAsync(this RequisitionsFixture fixture, Actor actor, Guid requisitionId, string action) =>
        fixture.Client(actor).PostAsync(Url($"/requisitions/{requisitionId}/{action}"), null);

    /// <summary>Raised and submitted over HTTP, and seen leaving for Budgets.</summary>
    public static async Task<RequisitionView> SubmittedAsync(
        this RequisitionsFixture fixture,
        Actor requester,
        decimal amount = 3_000m,
        string costCentre = RequisitionsFixture.Platform)
    {
        RequisitionView draft = await fixture.CreateAsync(requester, amount, costCentre);
        RequisitionView submitted = await (await fixture.PostAsync(requester, draft.Id, "submit")).ReadAsync<RequisitionView>();
        await fixture.Probe.WaitForAsync<RequisitionSubmitted>(submission => submission.RequisitionId == draft.Id);
        return submitted;
    }

    /// <summary>Submitted, then answered by Budgets with a reservation, so the route is fixed.</summary>
    public static async Task<RequisitionView> PendingApprovalAsync(
        this RequisitionsFixture fixture,
        Actor requester,
        decimal amount = 3_000m,
        string costCentre = RequisitionsFixture.Platform)
    {
        RequisitionView submitted = await fixture.SubmittedAsync(requester, amount, costCentre);
        await fixture.DeliverAsync(Reserved(submitted));

        RequisitionView pending = await fixture.GetAsync(requester, submitted.Id);
        pending.Status.ShouldBe(RequisitionStatus.PendingApproval);
        return pending;
    }

    public static FundsReserved Reserved(RequisitionView requisition) =>
        new(requisition.Id, requisition.CostCentreCode, requisition.FiscalYear!.Value, requisition.Amount, DateTimeOffset.UtcNow);

    public static async Task<RequisitionView> GetAsync(this RequisitionsFixture fixture, Actor actor, Guid requisitionId) =>
        await (await fixture.Client(actor).GetAsync(Url($"/requisitions/{requisitionId}"))).ReadAsync<RequisitionView>();

    public static async Task<Page<RequisitionSummary>> ApprovalsAsync(this RequisitionsFixture fixture, Actor approver, string query = "") =>
        await (await fixture.Client(approver).GetAsync(Url($"/approvals{query}"))).ReadAsync<Page<RequisitionSummary>>();

    /// <summary>Asserts the status, with the body in the failure message, and reads the body.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
}
