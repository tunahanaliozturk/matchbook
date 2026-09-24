using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Suppliers.Application;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Matchbook.Suppliers.Api;

/// <summary>
/// HTTP in, a handler call, HTTP out. Refusals are thrown as <c>BusinessRuleException</c> and turned into problem
/// details by the shared exception handler; the <c>Refuses</c> lists only tell the OpenAPI document about them.
/// </summary>
internal static class SupplierEndpoints
{
    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder suppliers = app.MapGroup("/suppliers")
            .WithTags("Suppliers")
            .Refuses(StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden);

        RouteGroupBuilder reads = suppliers.MapGroup("").RequireAuthorization(SupplierPolicies.Read);
        RouteGroupBuilder writes = suppliers.MapGroup("").RequireAuthorization(SupplierPolicies.Write);

        reads.MapGet("", ListAsync)
            .WithName("ListSuppliers")
            .WithSummary("Suppliers, oldest first, a page at a time")
            .ProducesValidationProblem();

        reads.MapGet("/{supplierId:guid}", GetAsync)
            .WithName("GetSupplier")
            .WithSummary("One supplier with its bank account history")
            .Refuses(StatusCodes.Status404NotFound);

        writes.MapPost("", CreateAsync)
            .WithName("CreateSupplier")
            .WithSummary("Record a new supplier as a draft (supplier-admin)")
            .ProducesValidationProblem()
            .Refuses(StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        writes.MapPut("/{supplierId:guid}", ChangeDetailsAsync)
            .WithName("ChangeSupplierDetails")
            .WithSummary("Correct the name, tax id, country, terms or contact (supplier-admin)")
            .ProducesValidationProblem()
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        writes.MapPost("/{supplierId:guid}/submit", SubmitAsync)
            .WithName("SubmitSupplier")
            .WithSummary("Send a draft for activation (supplier-admin)")
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        writes.MapPost("/{supplierId:guid}/activate", ActivateAsync)
            .WithName("ActivateSupplier")
            .WithSummary("Activate a pending supplier (supplier-approver who did not submit it)")
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        writes.MapPost("/{supplierId:guid}/block", BlockAsync)
            .WithName("BlockSupplier")
            .WithSummary("Stop ordering from and paying an active supplier (either supplier role)")
            .ProducesValidationProblem()
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        writes.MapPost("/{supplierId:guid}/unblock", UnblockAsync)
            .WithName("UnblockSupplier")
            .WithSummary("Lift a block (supplier-approver)")
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        writes.MapPost("/{supplierId:guid}/bank-accounts", ProposeBankAccountAsync)
            .WithName("ProposeBankAccount")
            .WithSummary("Propose a new bank account for a second person to approve (supplier-admin)")
            .ProducesValidationProblem()
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        writes.MapPost("/{supplierId:guid}/bank-accounts/{bankAccountId:guid}/approve", ApproveBankAccountAsync)
            .WithName("ApproveBankAccount")
            .WithSummary("Put a proposed account in force (supplier-approver who did not propose it)")
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        writes.MapPost("/{supplierId:guid}/bank-accounts/{bankAccountId:guid}/reject", RejectBankAccountAsync)
            .WithName("RejectBankAccount")
            .WithSummary("Turn a proposed account down with a reason (either supplier role)")
            .ProducesValidationProblem()
            .Refuses(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Ok<SupplierPageResponse>> ListAsync(
        SupplierStatus? status,
        bool? hasPendingBankAccount,
        Guid? after,
        int? limit,
        ListSuppliersHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new ListSuppliers(
            status is { } wanted ? Statuses.ToDomain(wanted) : null, hasPendingBankAccount, after, limit);

        return TypedResults.Ok(SupplierPageResponse.From(await handler.HandleAsync(query, cancellationToken)));
    }

    private static async Task<Ok<SupplierResponse>> GetAsync(
        Guid supplierId, ClaimsPrincipal user, GetSupplierHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(SupplierResponse.From(await handler.HandleAsync(supplierId, user.ToActor(), cancellationToken)));

    private static async Task<Created<SupplierResponse>> CreateAsync(
        CreateSupplierRequest request, ClaimsPrincipal user, CreateSupplierHandler handler, CancellationToken cancellationToken)
    {
        SupplierView supplier = await handler.HandleAsync(request.ToCommand(), user.ToActor(), cancellationToken);

        // Relative, so it resolves the same behind the gateway (/api/suppliers) as it does here (/suppliers).
        return TypedResults.Created($"suppliers/{supplier.Id}", SupplierResponse.From(supplier));
    }

    private static async Task<Ok<SupplierResponse>> ChangeDetailsAsync(
        Guid supplierId,
        ChangeSupplierDetailsRequest request,
        ClaimsPrincipal user,
        ChangeSupplierDetailsHandler handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(request.ToCommand(supplierId), user.ToActor(), cancellationToken));

    private static async Task<Ok<SupplierResponse>> SubmitAsync(
        Guid supplierId, ClaimsPrincipal user, SubmitSupplierHandler handler, CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new SubmitSupplier(supplierId), user.ToActor(), cancellationToken));

    private static async Task<Ok<SupplierResponse>> ActivateAsync(
        Guid supplierId, ClaimsPrincipal user, ActivateSupplierHandler handler, CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new ActivateSupplier(supplierId), user.ToActor(), cancellationToken));

    private static async Task<Ok<SupplierResponse>> BlockAsync(
        Guid supplierId,
        ReasonRequest request,
        ClaimsPrincipal user,
        BlockSupplierHandler handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new BlockSupplier(supplierId, request.Reason!), user.ToActor(), cancellationToken));

    private static async Task<Ok<SupplierResponse>> UnblockAsync(
        Guid supplierId, ClaimsPrincipal user, UnblockSupplierHandler handler, CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UnblockSupplier(supplierId), user.ToActor(), cancellationToken));

    private static async Task<Created<SupplierResponse>> ProposeBankAccountAsync(
        Guid supplierId,
        ProposeBankAccountRequest request,
        ClaimsPrincipal user,
        ProposeBankAccountHandler handler,
        CancellationToken cancellationToken)
    {
        SupplierView supplier = await handler.HandleAsync(request.ToCommand(supplierId), user.ToActor(), cancellationToken);

        // The proposal has no address of its own; it is read as part of the supplier.
        return TypedResults.Created((string?)null, SupplierResponse.From(supplier));
    }

    private static async Task<Ok<SupplierResponse>> ApproveBankAccountAsync(
        Guid supplierId,
        Guid bankAccountId,
        ClaimsPrincipal user,
        ApproveBankAccountHandler handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(
            new ApproveBankAccount(supplierId, bankAccountId), user.ToActor(), cancellationToken));

    private static async Task<Ok<SupplierResponse>> RejectBankAccountAsync(
        Guid supplierId,
        Guid bankAccountId,
        ReasonRequest request,
        ClaimsPrincipal user,
        RejectBankAccountHandler handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(
            new RejectBankAccount(supplierId, bankAccountId, request.Reason!), user.ToActor(), cancellationToken));

    private static Ok<SupplierResponse> Respond(SupplierView supplier) => TypedResults.Ok(SupplierResponse.From(supplier));

    private static TBuilder Refuses<TBuilder>(this TBuilder builder, params int[] statuses)
        where TBuilder : IEndpointConventionBuilder
    {
        foreach (int status in statuses)
        {
            builder.WithMetadata(new ProducesResponseTypeMetadata(status, typeof(ProblemDetails), ["application/problem+json"]));
        }

        return builder;
    }
}
