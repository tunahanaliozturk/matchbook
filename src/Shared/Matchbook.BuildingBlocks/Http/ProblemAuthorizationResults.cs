using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Matchbook.BuildingBlocks.Http;

/// <summary>
/// Gives the framework's own 401 and 403 the same shape as every other refusal, a problem with a stable
/// <c>code</c>, so a client handles "not signed in" and "not allowed" like any other answer it branches on.
/// </summary>
internal sealed class ProblemAuthorizationResults(IProblemDetailsService problems) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged)
        {
            await context.ChallengeAsync();
            await WriteAsync(context, StatusCodes.Status401Unauthorized, "auth.unauthenticated", "Sign in first: the request carries no valid token.");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await context.ForbidAsync();
            await WriteAsync(context, StatusCodes.Status403Forbidden, "auth.forbidden", "Your roles do not allow this.");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;

        return problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = status == StatusCodes.Status401Unauthorized ? "Not signed in." : "Not allowed.",
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        }).AsTask();
    }
}
