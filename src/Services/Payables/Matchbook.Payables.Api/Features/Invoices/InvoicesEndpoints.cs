using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Application.Features.Invoices.Commands.AcceptPriceVariance;
using Matchbook.Payables.Application.Features.Invoices.Commands.CaptureInvoice;
using Matchbook.Payables.Application.Features.Invoices.Commands.ClearSuspectedDuplicate;
using Matchbook.Payables.Application.Features.Invoices.Queries.GetInvoice;
using Matchbook.Payables.Application.Features.Invoices.Queries.ListBillablePurchaseOrders;
using Matchbook.Payables.Application.Features.Invoices.Queries.ListBillableSuppliers;
using Matchbook.Payables.Application.Features.Invoices.Queries.ListInvoiceExceptions;
using Matchbook.Payables.Application.Features.Invoices.Queries.ListInvoices;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Payables.Api.Features.Invoices;

internal static class InvoicesEndpoints
{
    public static IEndpointRouteBuilder MapInvoicesEndpoints(this IEndpointRouteBuilder app)
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

        // Reference data for the capture form: a clerk cannot read Suppliers or Purchasing, so Payables serves what it
        // holds of them (ADR 0009).
        invoices.MapGet("/suppliers", BillableSuppliers)
            .RequireAuthorization(Policies.CaptureInvoices)
            .WithName("ListBillableSuppliers")
            .WithSummary("Suppliers with an order an invoice can still bill, newest first, for capturing an invoice")
            .ProducesValidationProblem()
            .ProducesRefusals();

        invoices.MapGet("/purchase-orders", BillablePurchaseOrders)
            .RequireAuthorization(Policies.CaptureInvoices)
            .WithName("ListBillablePurchaseOrders")
            .WithSummary("Issued orders an invoice can still bill, with their lines, newest first, optionally for one supplier")
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

    // The three commands that claim the order's row run through OrderRaceRetry, which resolves the handler again in
    // a fresh scope for each attempt; that is why they take the retry rather than the handler.
    private static async Task<Created<InvoiceView>> Capture(
        CaptureInvoiceRequest request,
        ClaimsPrincipal user,
        OrderRaceRetry retry,
        CancellationToken cancellationToken)
    {
        CaptureInvoiceCommand command = request.ToCommand(user.ToActor());
        InvoiceView invoice = await retry.RunAsync<ICommandHandler<CaptureInvoiceCommand, InvoiceView>, InvoiceView>(
            handler => handler.HandleAsync(command, cancellationToken));
        return TypedResults.Created($"/invoices/{invoice.Id}", invoice);
    }

    private static async Task<Ok<Page<InvoiceSummary>>> List(
        InvoiceStatus? status,
        Guid? after,
        int? limit,
        IQueryHandler<ListInvoicesQuery, Page<InvoiceSummary>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListInvoicesQuery(status, after, limit ?? 0), cancellationToken));

    private static async Task<Ok<Page<InvoiceSummary>>> Exceptions(
        Guid? after,
        int? limit,
        IQueryHandler<ListInvoiceExceptionsQuery, Page<InvoiceSummary>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListInvoiceExceptionsQuery(after, limit ?? 0), cancellationToken));

    private static async Task<Ok<Page<BillableSupplier>>> BillableSuppliers(
        Guid? after,
        int? limit,
        IQueryHandler<ListBillableSuppliersQuery, Page<BillableSupplier>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListBillableSuppliersQuery(after, limit ?? 0), cancellationToken));

    private static async Task<Ok<Page<BillablePurchaseOrder>>> BillablePurchaseOrders(
        Guid? supplierId,
        Guid? after,
        int? limit,
        IQueryHandler<ListBillablePurchaseOrdersQuery, Page<BillablePurchaseOrder>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListBillablePurchaseOrdersQuery(supplierId, after, limit ?? 0), cancellationToken));

    private static async Task<Ok<InvoiceView>> Get(
        Guid id,
        IQueryHandler<GetInvoiceQuery, InvoiceView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetInvoiceQuery(id), cancellationToken));

    private static async Task<Ok<InvoiceView>> AcceptPriceVariance(
        Guid id,
        AcceptPriceVarianceRequest request,
        ClaimsPrincipal user,
        OrderRaceRetry retry,
        CancellationToken cancellationToken)
    {
        var command = new AcceptPriceVarianceCommand(id, request.Reason!, user.ToActor());
        return TypedResults.Ok(await retry.RunAsync<ICommandHandler<AcceptPriceVarianceCommand, InvoiceView>, InvoiceView>(
            handler => handler.HandleAsync(command, cancellationToken)));
    }

    private static async Task<Ok<InvoiceView>> ClearSuspectedDuplicate(
        Guid id,
        ClaimsPrincipal user,
        OrderRaceRetry retry,
        CancellationToken cancellationToken)
    {
        var command = new ClearSuspectedDuplicateCommand(id, user.ToActor());
        return TypedResults.Ok(await retry.RunAsync<ICommandHandler<ClearSuspectedDuplicateCommand, InvoiceView>, InvoiceView>(
            handler => handler.HandleAsync(command, cancellationToken)));
    }
}
