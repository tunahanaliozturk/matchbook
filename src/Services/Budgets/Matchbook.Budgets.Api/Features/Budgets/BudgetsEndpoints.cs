using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Application.Features.Budgets;
using Matchbook.Budgets.Application.Features.Budgets.Commands.ChangeAllotment;
using Matchbook.Budgets.Application.Features.Budgets.Commands.OpenBudget;
using Matchbook.Budgets.Application.Features.Budgets.Queries.GetBudget;
using Matchbook.Budgets.Application.Features.Budgets.Queries.ListBudgets;
using Matchbook.Budgets.Application.Features.Budgets.Queries.ListLedger;
using Matchbook.Budgets.Application.Features.Budgets.Queries.OverspendReport;
using Matchbook.Budgets.Domain;
using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Matchbook.Budgets.Api.Features.Budgets;

internal static class BudgetsEndpoints
{
    public static IEndpointRouteBuilder MapBudgetsEndpoints(this IEndpointRouteBuilder app)
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
        OpenBudgetRequest request,
        ClaimsPrincipal user,
        ICommandHandler<OpenBudgetCommand, BudgetView> handler,
        CancellationToken cancellationToken)
    {
        BudgetView opened = await handler.HandleAsync(
            new OpenBudgetCommand(request.Id, request.CostCentreCode, request.FiscalYear, request.Allotted, user.ToActor()),
            cancellationToken);
        return TypedResults.Created($"/budgets/{opened.Id}", opened);
    }

    private static async Task<Ok<Page<BudgetView>>> ListAsync(
        [FromQuery, Range(Budget.EarliestFiscalYear, Budget.LatestFiscalYear)] int fiscalYear,
        [FromQuery] string? after,
        [FromQuery, Range(1, 200)] int? limit,
        IQueryHandler<ListBudgetsQuery, Page<BudgetView>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListBudgetsQuery(fiscalYear, after, limit), cancellationToken));

    private static async Task<Ok<Page<BudgetView>>> OverspendsAsync(
        [FromQuery, Range(Budget.EarliestFiscalYear, Budget.LatestFiscalYear)] int fiscalYear,
        [FromQuery] string? after,
        [FromQuery, Range(1, 200)] int? limit,
        IQueryHandler<OverspendReportQuery, Page<BudgetView>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new OverspendReportQuery(fiscalYear, after, limit), cancellationToken));

    private static async Task<Ok<BudgetView>> GetAsync(
        Guid id, IQueryHandler<GetBudgetQuery, BudgetView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetBudgetQuery(id), cancellationToken));

    // No Location: a change has no address of its own. It shows up in the ledger under its id.
    private static async Task<Created<AllotmentChangeView>> ChangeAllotmentAsync(
        Guid id,
        ChangeAllotmentRequest request,
        ClaimsPrincipal user,
        ICommandHandler<ChangeAllotmentCommand, AllotmentChangeView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Created(
            (string?)null,
            await handler.HandleAsync(
                new ChangeAllotmentCommand(request.Id, id, request.Change, user.ToActor()), cancellationToken));

    private static async Task<Ok<Page<LedgerEntryView>>> LedgerAsync(
        Guid id,
        [FromQuery] long? after,
        [FromQuery, Range(1, 200)] int? limit,
        IQueryHandler<ListLedgerQuery, Page<LedgerEntryView>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListLedgerQuery(id, after, limit), cancellationToken));
}
