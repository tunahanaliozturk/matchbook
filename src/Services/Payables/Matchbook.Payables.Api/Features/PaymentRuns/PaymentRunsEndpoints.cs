using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Payables.Application.Features.PaymentRuns;
using Matchbook.Payables.Application.Features.PaymentRuns.Commands.CancelPaymentRun;
using Matchbook.Payables.Application.Features.PaymentRuns.Commands.DraftPaymentRun;
using Matchbook.Payables.Application.Features.PaymentRuns.Commands.ReleasePaymentRun;
using Matchbook.Payables.Application.Features.PaymentRuns.Queries.DownloadPaymentFile;
using Matchbook.Payables.Application.Features.PaymentRuns.Queries.GetPaymentRun;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Payables.Api.Features.PaymentRuns;

internal static class PaymentRunsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentRunsEndpoints(this IEndpointRouteBuilder app)
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
        ICommandHandler<DraftPaymentRunCommand, PaymentRunView> handler,
        CancellationToken cancellationToken)
    {
        PaymentRunView run = await handler.HandleAsync(
            new DraftPaymentRunCommand(request.Id, request.ExecutionDate!.Value, user.ToActor()),
            cancellationToken);
        return TypedResults.Created($"/payment-runs/{run.Id}", run);
    }

    private static async Task<Ok<PaymentRunView>> Get(
        Guid id,
        IQueryHandler<GetPaymentRunQuery, PaymentRunView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetPaymentRunQuery(id), cancellationToken));

    private static async Task<Ok<PaymentRunView>> Release(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<ReleasePaymentRunCommand, PaymentRunView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ReleasePaymentRunCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<PaymentRunView>> Cancel(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<CancelPaymentRunCommand, PaymentRunView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new CancelPaymentRunCommand(id, user.ToActor()), cancellationToken));

    // The checks that can refuse the download run before the first byte, so a refusal is still a problem response;
    // after that the file goes from the database to the response as it is written.
    private static async Task<PushStreamHttpResult> Download(
        Guid id,
        IQueryHandler<DownloadPaymentFileQuery, PaymentFile> handler,
        CancellationToken cancellationToken)
    {
        PaymentFile file = await handler.HandleAsync(new DownloadPaymentFileQuery(id), cancellationToken);
        return TypedResults.Stream(
            destination => file.WriteToAsync(destination, cancellationToken),
            PaymentFile.ContentType,
            file.FileName);
    }
}
