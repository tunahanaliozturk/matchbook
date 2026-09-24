using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Payables.Application.PaymentRuns;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Payables.Api.PaymentRuns;

/// <summary>A payment run to draft for an execution date.</summary>
/// <param name="Id">Optional. Repeating a draft with the same id returns the run it created instead of a second one.</param>
public sealed record DraftPaymentRunRequest(Guid? Id, [Required] DateOnly? ExecutionDate);

internal static class PaymentRunEndpoints
{
    public static IEndpointRouteBuilder MapPaymentRuns(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder runs = app.MapGroup("/payment-runs").WithTags("Payment runs");

        runs.MapPost("/", Draft)
            .RequireAuthorization(Policies.ManagePaymentRuns)
            .WithName("DraftPaymentRun")
            .WithSummary("Draft a run of every payable invoice due by the execution date, for suppliers that can be paid")
            .ProducesValidationProblem()
            .ProducesRefusals(StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        runs.MapGet("/{id:guid}", Get)
            .RequireAuthorization(Policies.ReadPaymentRuns)
            .WithName("GetPaymentRun")
            .WithSummary("A payment run, per supplier, with account numbers masked")
            .ProducesRefusals(StatusCodes.Status404NotFound);

        runs.MapPost("/{id:guid}/release", Release)
            .RequireAuthorization(Policies.ManagePaymentRuns)
            .WithName("ReleasePaymentRun")
            .WithSummary("Release a draft run: a treasurer other than the one who drafted it")
            .ProducesRefusals(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        runs.MapPost("/{id:guid}/cancel", Cancel)
            .RequireAuthorization(Policies.ManagePaymentRuns)
            .WithName("CancelPaymentRun")
            .WithSummary("Cancel a draft run; its invoices become payable again")
            .ProducesRefusals(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        runs.MapGet("/{id:guid}/file", Download)
            .RequireAuthorization(Policies.ManagePaymentRuns)
            .WithName("DownloadPaymentFile")
            .WithSummary("The released run's ISO 20022 pain.001.001.09 credit transfer file")
            .Produces<Stream>(StatusCodes.Status200OK, PaymentFile.ContentType)
            .ProducesRefusals(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Created<PaymentRunView>> Draft(
        DraftPaymentRunRequest request,
        ClaimsPrincipal user,
        DraftPaymentRunHandler handler,
        CancellationToken cancellationToken)
    {
        PaymentRunView run = await handler.HandleAsync(
            new DraftPaymentRun(request.Id, request.ExecutionDate!.Value),
            user.ToActor(),
            cancellationToken);
        return TypedResults.Created($"/payment-runs/{run.Id}", run);
    }

    private static async Task<Ok<PaymentRunView>> Get(Guid id, GetPaymentRunHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, cancellationToken));

    private static async Task<Ok<PaymentRunView>> Release(
        Guid id,
        ClaimsPrincipal user,
        ReleasePaymentRunHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, user.ToActor(), cancellationToken));

    private static async Task<Ok<PaymentRunView>> Cancel(
        Guid id,
        ClaimsPrincipal user,
        CancelPaymentRunHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, user.ToActor(), cancellationToken));

    // The checks that can refuse the download run before the first byte, so a refusal is still a problem response;
    // after that the file goes from the database to the response as it is written.
    private static async Task<PushStreamHttpResult> Download(
        Guid id,
        DownloadPaymentFileHandler handler,
        CancellationToken cancellationToken)
    {
        PaymentFile file = await handler.HandleAsync(id, cancellationToken);
        return TypedResults.Stream(
            destination => file.WriteToAsync(destination, cancellationToken),
            PaymentFile.ContentType,
            file.FileName);
    }
}
