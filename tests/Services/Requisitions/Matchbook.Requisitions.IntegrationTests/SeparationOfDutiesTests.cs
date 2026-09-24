using System.Net;
using System.Net.Http.Json;
using Matchbook.Requisitions.Api.Features.Requisitions;
using Matchbook.Requisitions.Application.Features.Requisitions;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>The separation-of-duties rules as a client meets them: over HTTP, with their status and code.</summary>
public sealed class SeparationOfDutiesTests(RequisitionsFixture fixture) : IClassFixture<RequisitionsFixture>
{
    [Fact]
    public async Task Nobody_approves_their_own_requisition()
    {
        Actor requesterInFinance = TestUsers.Stranger(Roles.Requester, Roles.FinanceApprover);
        RequisitionView pending = await fixture.PendingApprovalAsync(requesterInFinance, 50_000m);
        await (await fixture.PostAsync(TestUsers.Mark, pending.Id, "approve")).ReadAsync<RequisitionView>();

        HttpResponseMessage own = await fixture.PostAsync(requesterInFinance, pending.Id, "approve");

        await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, RequisitionCodes.SelfApproval);
    }

    [Fact]
    public async Task Nobody_approves_two_steps_of_one_requisition()
    {
        Actor managerInFinance = TestUsers.Stranger(Roles.Approver, Roles.FinanceApprover);
        string costCentre = await fixture.CostCentreManagedByAsync(managerInFinance.Id);
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita, 50_000m, costCentre);
        await (await fixture.PostAsync(managerInFinance, pending.Id, "approve")).ReadAsync<RequisitionView>();

        HttpResponseMessage second = await fixture.PostAsync(managerInFinance, pending.Id, "approve");

        await second.ShouldBeProblemAsync(HttpStatusCode.Forbidden, RequisitionCodes.DuplicateApprover);
        (await (await fixture.PostAsync(TestUsers.Fiona, pending.Id, "approve")).ReadAsync<RequisitionView>())
            .Status.ShouldBe(RequisitionStatus.Approved);
    }

    [Fact]
    public async Task Only_the_manager_on_record_takes_the_manager_step()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita, 50_000m);

        await (await fixture.PostAsync(TestUsers.Maya, pending.Id, "approve"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, RequisitionCodes.NotYourStep);
        await (await fixture.PostAsync(TestUsers.Fiona, pending.Id, "approve"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, RequisitionCodes.NotYourStep);
        await (await fixture.Client(TestUsers.Maya).PostAsJsonAsync(
                Scenario.Url($"/requisitions/{pending.Id}/reject"), new RejectRequisitionRequest("Not my cost centre's money"), Scenario.Json))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, RequisitionCodes.NotYourStep);
    }

    [Fact]
    public async Task A_manager_cannot_raise_a_requisition_against_their_own_cost_centre()
    {
        Actor managerWhoRequests = TestUsers.Stranger(Roles.Requester, Roles.Approver);
        string costCentre = await fixture.CostCentreManagedByAsync(managerWhoRequests.Id);
        RequisitionView draft = await fixture.CreateAsync(managerWhoRequests, 5_000m, costCentre);

        HttpResponseMessage submit = await fixture.PostAsync(managerWhoRequests, draft.Id, "submit");

        await submit.ShouldBeProblemAsync(HttpStatusCode.Conflict, RequisitionCodes.RequesterIsManager);
    }
}
