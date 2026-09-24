using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Application.Budgets;
using Matchbook.Budgets.Domain;
using Matchbook.BuildingBlocks.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Matchbook.Budgets.Api;

internal static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgets(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/budgets")
            .WithTags("Budgets")
            .RequireAuthorization(Policies.ReadBudgets)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", OpenAsync)
            .RequireAuthorization(Policies.Administer)
            .WithName("OpenBudget")
            .WithSummary("Open a cost centre's budget for a fiscal year")
            .WithDescription("Idempotent on the optional id, which becomes the budget's id.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAsync)
            .WithName("ListBudgets")
            .WithSummary("List one fiscal year's budgets in cost centre order")
            .ProducesValidationProblem();

        group.MapGet("/overspends", OverspendsAsync)
            .WithName("ListOverspentBudgets")
            .WithSummary("List one fiscal year's budgets whose consumption has run past the allotment")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetBudget")
            .WithSummary("Get a budget's balance: the four figures, available and overspend")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/allotment-changes", ChangeAllotmentAsync)
            .RequireAuthorization(Policies.Administer)
            .WithName("ChangeAllotment")
            .WithSummary("Raise or lower a budget's allotment")
            .WithDescription("Lowering stops at what is already reserved, committed or spent. Idempotent on the optional id.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}/ledger", LedgerAsync)
            .WithName("ListLedger")
            .WithSummary("Page through a budget's ledger in the order it was written")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Created<BudgetView>> OpenAsync(
        OpenBudgetRequest request, ClaimsPrincipal user, OpenBudgetHandler handler, CancellationToken cancellationToken)
    {
        BudgetView opened = await handler.HandleAsync(
            new OpenBudget(request.Id, request.CostCentreCode, request.FiscalYear, request.Allotted),
            user.ToActor(),
            cancellationToken);
        return TypedResults.Created($"/budgets/{opened.Id}", opened);
    }

    private static async Task<Ok<Page<BudgetView>>> ListAsync(
        [FromQuery, Range(Budget.EarliestFiscalYear, Budget.LatestFiscalYear)] int fiscalYear,
        [FromQuery] string? after,
        [FromQuery, Range(1, 200)] int? limit,
        ListBudgetsHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListBudgets(fiscalYear, after, limit), cancellationToken));

    private static async Task<Ok<Page<BudgetView>>> OverspendsAsync(
        [FromQuery, Range(Budget.EarliestFiscalYear, Budget.LatestFiscalYear)] int fiscalYear,
        [FromQuery] string? after,
        [FromQuery, Range(1, 200)] int? limit,
        OverspendReportHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new OverspendReport(fiscalYear, after, limit), cancellationToken));

    private static async Task<Ok<BudgetView>> GetAsync(Guid id, GetBudgetHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(id, cancellationToken));

    // No Location: a change has no address of its own. It shows up in the ledger under its id.
    private static async Task<Created<AllotmentChangeView>> ChangeAllotmentAsync(
        Guid id,
        ChangeAllotmentRequest request,
        ClaimsPrincipal user,
        ChangeAllotmentHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Created(
            (string?)null,
            await handler.HandleAsync(new ChangeAllotment(request.Id, id, request.Change), user.ToActor(), cancellationToken));

    private static async Task<Ok<Page<LedgerEntryView>>> LedgerAsync(
        Guid id,
        [FromQuery] long? after,
        [FromQuery, Range(1, 200)] int? limit,
        ListLedgerHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListLedger(id, after, limit), cancellationToken));
}
