using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.BuildingBlocks.Http;

/// <summary>
/// Turns the exceptions that mean "no" into RFC 9457 problem details with a stable <c>code</c>, so every service
/// refuses the same way and a client branches on the code rather than on the wording.
/// </summary>
/// <remarks>
/// Anything else is left to the default handler, which answers 500 without detail. An unexpected exception is
/// logged by the framework, and its message never reaches the caller.
/// </remarks>
internal sealed class ProblemMapping(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string code, string detail)? refusal = exception switch
        {
            BusinessRuleException rule => (StatusFor(rule.Kind), rule.Code, rule.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency.conflict",
                "Someone else changed this since you read it. Read it again and retry."),
            BadHttpRequestException bad => (bad.StatusCode, "request.malformed", "The request could not be read."),
            _ => null,
        };

        if (refusal is not { } problem)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.status;

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = problem.status,
                Title = TitleFor(problem.status),
                Detail = problem.detail,
                Extensions = { ["code"] = problem.code },
            },
        });
    }

    private static int StatusFor(ViolationKind kind) => kind switch
    {
        ViolationKind.Invalid => StatusCodes.Status422UnprocessableEntity,
        ViolationKind.NotFound => StatusCodes.Status404NotFound,
        ViolationKind.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status409Conflict,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "The request is malformed.",
        StatusCodes.Status403Forbidden => "Not allowed.",
        StatusCodes.Status404NotFound => "Not found.",
        StatusCodes.Status422UnprocessableEntity => "The request breaks a rule.",
        _ => "The current state does not allow this.",
    };
}
