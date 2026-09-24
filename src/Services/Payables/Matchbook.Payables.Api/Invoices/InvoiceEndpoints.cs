using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Payables.Application;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Domain.Invoices;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Payables.Api.Invoices;

internal static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoices(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder invoices = app.MapGroup("/invoices").WithTags("Invoices");

        invoices.MapPost("/", Capture)
            .RequireAuthorization(Policies.CaptureInvoices)
            .WithName("CaptureInvoice")
            .WithSummary("Capture a supplier invoice and match it against its order and receipts")
            .ProducesValidationProblem()
            .ProducesRefusals(StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        invoices.MapGet("/", List)
            .RequireAuthorization(Policies.ReadInvoices)
            .WithName("ListInvoices")
            .WithSummary("List invoices, newest first, optionally in one status")
            .ProducesValidationProblem()
            .ProducesRefusals();

        invoices.MapGet("/exceptions", Exceptions)
            .RequireAuthorization(Policies.ReadInvoices)
            .WithName("ListInvoiceExceptions")
            .WithSummary("Invoices waiting for an approver: suspected duplicates and price variances, oldest first")
            .ProducesValidationProblem()
            .ProducesRefusals();

        invoices.MapGet("/{id:guid}", Get)
            .RequireAuthorization(Policies.ReadInvoices)
            .WithName("GetInvoice")
            .WithSummary("An invoice, with why it is in its current state")
            .ProducesRefusals(StatusCodes.Status404NotFound);

        invoices.MapPost("/{id:guid}/accept-price-variance", AcceptPriceVariance)
            .RequireAuthorization(Policies.ReviewInvoices)
            .WithName("AcceptPriceVariance")
            .WithSummary("Accept a price outside tolerance; only an AP approver who did not capture the invoice")
            .ProducesValidationProblem()
            .ProducesRefusals(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        invoices.MapPost("/{id:guid}/clear-suspected-duplicate", ClearSuspectedDuplicate)
            .RequireAuthorization(Policies.ReviewInvoices)
            .WithName("ClearSuspectedDuplicate")
            .WithSummary("Confirm an invoice held as a suspected duplicate is genuine; only an AP approver who did not capture it")
            .ProducesRefusals(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Created<InvoiceView>> Capture(
        CaptureInvoiceRequest request,
        ClaimsPrincipal user,
        CaptureInvoiceHandler handler,
        CancellationToken cancellationToken)
    {
        InvoiceView invoice = await handler.HandleAsync(request.ToCommand(), user.ToActor(), cancellationToken);
        return TypedResults.Created($"/invoices/{invoice.Id}", invoice);
    }

    private static async Task<Ok<Page<InvoiceSummary>>> List(
        InvoiceStatus? status,
        Guid? after,
        int? limit,
        ListInvoicesHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListInvoices(status, after, limit ?? 0), cancellationToken));

    private static async Task<Ok<Page<InvoiceSummary>>> Exceptions(
        Guid? after,
        int? limit,
        ListInvoiceExceptionsHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(after, limit ?? 0, cancellationToken));

    private static async Task<Ok<InvoiceView>> Get(Guid id, GetInvoiceHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, cancellationToken));

    private static async Task<Ok<InvoiceView>> AcceptPriceVariance(
        Guid id,
        AcceptPriceVarianceRequest request,
        ClaimsPrincipal user,
        AcceptPriceVarianceHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, request.Reason!, user.ToActor(), cancellationToken));

    private static async Task<Ok<InvoiceView>> ClearSuspectedDuplicate(
        Guid id,
        ClaimsPrincipal user,
        ClearSuspectedDuplicateHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, user.ToActor(), cancellationToken));
}
