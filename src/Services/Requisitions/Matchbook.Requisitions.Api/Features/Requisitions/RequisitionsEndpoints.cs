using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Application.Features.Requisitions;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.ApproveRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.CancelRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.CreateRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.EditRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.RejectRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Commands.SubmitRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Queries.GetRequisition;
using Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListRequisitions;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Requisitions.Api.Features.Requisitions;

/// <summary>
/// HTTP in, a handler call, HTTP out. Refusals are exceptions the shared problem mapping turns into problem
/// details, so each endpoint lists the problems it can answer with for the OpenAPI document.
/// </summary>
internal static class RequisitionsEndpoints
{
    public static IEndpointRouteBuilder MapRequisitionsEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder requisitions = routes.MapGroup("/requisitions")
            .WithTags("Requisitions")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requisitions.MapPost("", CreateAsync)
            .RequireAuthorization(Policies.Request)
            .WithName("CreateRequisition")
            .WithSummary("Draft a requisition.")
            .WithDescription("Role: requester. Idempotent on the optional id in the body.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        requisitions.MapGet("", ListAsync)
            .RequireAuthorization(Policies.Read)
            .WithName("ListRequisitions")
            .WithSummary("The requisitions the caller may read, newest first.")
            .WithDescription("A requester sees their own; approvers and the auditor see all. Pass the previous page's nextCursor as after; limit is 1 to 200, default 50.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        requisitions.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(Policies.Read)
            .WithName("GetRequisition")
            .WithSummary("A requisition with its lines, approval route and timeline.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        requisitions.MapPut("/{id:guid}", EditAsync)
            .RequireAuthorization(Policies.Request)
            .WithName("EditRequisition")
            .WithSummary("Replace what a draft says.")
            .WithDescription("Role: requester, and only the requisition's own requester, while it is a draft.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        requisitions.MapPost("/{id:guid}/submit", SubmitAsync)
            .RequireAuthorization(Policies.Request)
            .WithName("SubmitRequisition")
            .WithSummary("Send a draft to Budgets for a reservation.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequireAuthorization(Policies.Request)
            .WithName("CancelRequisition")
            .WithSummary("Withdraw a requisition before it is approved.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(Policies.Decide)
            .WithName("ApproveRequisition")
            .WithSummary("Sign off the requisition's current approval step.")
            .WithDescription("Roles: approver, finance-approver, cfo. Nobody approves their own requisition or two steps of one.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(Policies.Decide)
            .WithName("RejectRequisition")
            .WithSummary("Refuse the requisition at its current approval step, with a reason.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return routes;
    }

    private static async Task<Created<RequisitionView>> CreateAsync(
        CreateRequisitionRequest request,
        ClaimsPrincipal user,
        ICommandHandler<CreateRequisitionCommand, RequisitionView> handler,
        CancellationToken cancellationToken)
    {
        RequisitionView created = await handler.HandleAsync(
            new CreateRequisitionCommand(request.Id, request.ToDetails(), user.ToActor()), cancellationToken);
        return TypedResults.Created($"/requisitions/{created.Id}", created);
    }

    private static async Task<Ok<Page<RequisitionSummary>>> ListAsync(
        long? after,
        int? limit,
        ClaimsPrincipal user,
        IQueryHandler<ListRequisitionsQuery, Page<RequisitionSummary>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListRequisitionsQuery(after, limit, user.ToActor()), cancellationToken));

    private static async Task<Ok<RequisitionView>> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        IQueryHandler<GetRequisitionQuery, RequisitionView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetRequisitionQuery(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<RequisitionView>> EditAsync(
        Guid id,
        EditRequisitionRequest request,
        ClaimsPrincipal user,
        ICommandHandler<EditRequisitionCommand, RequisitionView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(
            new EditRequisitionCommand(id, request.ToDetails(), user.ToActor()), cancellationToken));

    private static async Task<Ok<RequisitionView>> SubmitAsync(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<SubmitRequisitionCommand, RequisitionView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new SubmitRequisitionCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<RequisitionView>> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<CancelRequisitionCommand, RequisitionView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new CancelRequisitionCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<RequisitionView>> ApproveAsync(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<ApproveRequisitionCommand, RequisitionView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ApproveRequisitionCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<RequisitionView>> RejectAsync(
        Guid id,
        RejectRequisitionRequest request,
        ClaimsPrincipal user,
        ICommandHandler<RejectRequisitionCommand, RequisitionView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(
            new RejectRequisitionCommand(id, request.Reason, user.ToActor()), cancellationToken));
}
