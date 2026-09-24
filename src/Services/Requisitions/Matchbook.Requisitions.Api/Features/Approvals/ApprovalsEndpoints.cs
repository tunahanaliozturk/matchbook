using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Application.Features.Approvals.Queries.ListMyApprovals;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Requisitions.Api.Features.Approvals;

internal static class ApprovalsEndpoints
{
    public static IEndpointRouteBuilder MapApprovalsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/approvals", ListAsync)
            .RequireAuthorization(Policies.Decide)
            .WithTags("Approvals")
            .WithName("ListApprovals")
            .WithSummary("The requisitions the caller can approve or reject right now, longest waiting first.")
            .WithDescription("Roles: approver, finance-approver, cfo. Decide one with POST /requisitions/{id}/approve or /reject. Pass the previous page's nextCursor as after; limit is 1 to 200, default 50.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return routes;
    }

    private static async Task<Ok<Page<RequisitionSummary>>> ListAsync(
        long? after,
        int? limit,
        ClaimsPrincipal user,
        IQueryHandler<ListMyApprovalsQuery, Page<RequisitionSummary>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListMyApprovalsQuery(after, limit, user.ToActor()), cancellationToken));
}
