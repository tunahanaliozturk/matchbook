using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Requisitions.Application.UseCases;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Requisitions.Api;

internal static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovals(this IEndpointRouteBuilder routes)
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
        ListMyApprovalsHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(user.ToActor(), after, limit, cancellationToken));
}
