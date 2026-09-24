using System.Security.Claims;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.AmendDraftLine;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.IssuePurchaseOrder;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.RecordReceipt;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.ShortClosePurchaseOrder;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetPurchaseOrder;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderForRequisition;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetReceipt;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListPurchaseOrders;
using Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListReceipts;
using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Matchbook.Purchasing.Api.Features.PurchaseOrders;

/// <summary>
/// <c>/purchase-orders</c>. Each endpoint turns HTTP into one command or query and back. Refusals are exceptions the
/// shared problem mapping answers, so the success type is the only result an endpoint returns itself.
/// </summary>
/// <remarks>
/// The Application views are the response bodies. They exist only to be returned from here, so an Api copy of
/// each would be a field-for-field mapping with nothing to protect.
/// </remarks>
internal static class PurchaseOrdersEndpoints
{
    private const int DefaultLimit = 50;

    public static IEndpointRouteBuilder MapPurchaseOrders(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder orders = app.MapGroup("/purchase-orders")
            .WithTags("Purchase orders")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        orders.MapGet("/", ListAsync)
            .WithName("ListPurchaseOrders")
            .WithSummary("Orders, newest first, optionally of one status or only those still awaiting goods. Pass the page's next cursor as after.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(Policies.Read);

        orders.MapGet("/{id:guid}", GetAsync)
            .WithName("GetPurchaseOrder")
            .WithSummary("One order with what was received and invoiced on each line.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.Read);

        orders.MapGet("/by-requisition/{requisitionId:guid}", GetForRequisitionAsync)
            .WithName("GetPurchaseOrderForRequisition")
            .WithSummary("The order drafted for a requisition. 404 until the approval has been processed.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.Read);

        orders.MapPut("/{id:guid}/lines/{lineNumber:int}", AmendLineAsync)
            .WithName("AmendPurchaseOrderLine")
            .WithSummary("Change the quantity and unit price of a line on a draft.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(Policies.Buy);

        orders.MapPost("/{id:guid}/issue", IssueAsync)
            .WithName("IssuePurchaseOrder")
            .WithSummary("Send a draft to Budgets for commitment. It is issued when the funds are committed.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(Policies.Buy);

        orders.MapPost("/{id:guid}/short-close", ShortCloseAsync)
            .WithName("ShortClosePurchaseOrder")
            .WithSummary("Close an issued order early. Nothing more will be received; invoices still count.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(Policies.Buy);

        orders.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelPurchaseOrder")
            .WithSummary("Cancel a draft, an order waiting for commitment, or an issued order with nothing received.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(Policies.Buy);

        orders.MapPost("/{id:guid}/receipts", RecordReceiptAsync)
            .WithName("RecordGoodsReceipt")
            .WithSummary("Record goods received. Idempotent on the receipt id the client sends.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(Policies.Receive);

        orders.MapGet("/{id:guid}/receipts", ListReceiptsAsync)
            .WithName("ListGoodsReceipts")
            .WithSummary("Every receipt against the order, oldest first.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.Read);

        orders.MapGet("/{id:guid}/receipts/{receiptId:guid}", GetReceiptAsync)
            .WithName("GetGoodsReceipt")
            .WithSummary("One receipt.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.Read);

        return app;
    }

    private static async Task<Ok<PurchaseOrderPage>> ListAsync(
        PurchaseOrderStatus? status,
        bool? awaitingGoods,
        Guid? after,
        int? limit,
        IQueryHandler<ListPurchaseOrdersQuery, PurchaseOrderPage> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(
            new ListPurchaseOrdersQuery(status, awaitingGoods, after, limit ?? DefaultLimit), cancellationToken));

    private static async Task<Ok<PurchaseOrderView>> GetAsync(
        Guid id, IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetPurchaseOrderQuery(id), cancellationToken));

    private static async Task<Ok<PurchaseOrderView>> GetForRequisitionAsync(
        Guid requisitionId,
        IQueryHandler<GetPurchaseOrderForRequisitionQuery, PurchaseOrderView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetPurchaseOrderForRequisitionQuery(requisitionId), cancellationToken));

    private static async Task<Ok<PurchaseOrderView>> AmendLineAsync(
        Guid id,
        int lineNumber,
        AmendLineRequest request,
        ClaimsPrincipal user,
        ICommandHandler<AmendDraftLineCommand, PurchaseOrderView> handler,
        CancellationToken cancellationToken)
    {
        // Validation has run: both values are present.
        var command = new AmendDraftLineCommand(id, lineNumber, request.Quantity!.Value, request.UnitPrice!.Value, user.ToActor());
        return TypedResults.Ok(await handler.HandleAsync(command, cancellationToken));
    }

    // 202: the order now waits for Budgets, and becomes Issued when the commitment is confirmed.
    private static async Task<Accepted<PurchaseOrderView>> IssueAsync(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<IssuePurchaseOrderCommand, PurchaseOrderView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Accepted((string?)null, await handler.HandleAsync(new IssuePurchaseOrderCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<PurchaseOrderView>> ShortCloseAsync(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<ShortClosePurchaseOrderCommand, PurchaseOrderView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ShortClosePurchaseOrderCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Ok<PurchaseOrderView>> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        ICommandHandler<CancelPurchaseOrderCommand, PurchaseOrderView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new CancelPurchaseOrderCommand(id, user.ToActor()), cancellationToken));

    private static async Task<Created<GoodsReceiptView>> RecordReceiptAsync(
        Guid id,
        RecordReceiptRequest request,
        ClaimsPrincipal user,
        ICommandHandler<RecordReceiptCommand, GoodsReceiptView> handler,
        CancellationToken cancellationToken)
    {
        // Validation has run: the list and every value in it are present.
        var command = new RecordReceiptCommand(
            id,
            request.Id,
            [.. request.Lines!.Select(static line => new LineQuantity(line.LineNumber!.Value, line.Quantity!.Value))],
            user.ToActor());

        GoodsReceiptView receipt = await handler.HandleAsync(command, cancellationToken);

        // Relative to the request (.../purchase-orders/{id}/receipts), so it resolves the same whether the caller
        // came through the gateway's /api prefix or straight to the service.
        return TypedResults.Created($"receipts/{receipt.Id}", receipt);
    }

    private static async Task<Ok<IReadOnlyList<GoodsReceiptView>>> ListReceiptsAsync(
        Guid id, IQueryHandler<ListReceiptsQuery, IReadOnlyList<GoodsReceiptView>> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListReceiptsQuery(id), cancellationToken));

    private static async Task<Ok<GoodsReceiptView>> GetReceiptAsync(
        Guid id, Guid receiptId, IQueryHandler<GetReceiptQuery, GoodsReceiptView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetReceiptQuery(id, receiptId), cancellationToken));
}
