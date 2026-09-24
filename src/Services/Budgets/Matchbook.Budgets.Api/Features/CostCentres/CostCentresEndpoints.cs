using System.ComponentModel.DataAnnotations;
using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Application.Features.CostCentres;
using Matchbook.Budgets.Application.Features.CostCentres.Commands.ChangeCostCentre;
using Matchbook.Budgets.Application.Features.CostCentres.Commands.CreateCostCentre;
using Matchbook.Budgets.Application.Features.CostCentres.Queries.GetCostCentre;
using Matchbook.Budgets.Application.Features.CostCentres.Queries.ListCostCentres;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Matchbook.Budgets.Api.Features.CostCentres;

internal static class CostCentresEndpoints
{
    public static IEndpointRouteBuilder MapCostCentresEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/cost-centres")
            .WithTags("Cost centres")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Policies.Administer)
            .WithName("CreateCostCentre")
            .WithSummary("Create a cost centre")
            .WithDescription("Publishes CostCentreChanged at version 1. Idempotent on the optional id.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAsync)
            .WithName("ListCostCentres")
            .WithSummary("List cost centres in code order")
            .ProducesValidationProblem();

        group.MapGet("/{code}", GetAsync)
            .WithName("GetCostCentre")
            .WithSummary("Get a cost centre")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{code}", ChangeAsync)
            .RequireAuthorization(Policies.Administer)
            .WithName("ChangeCostCentre")
            .WithSummary("Rename, reassign or (de)activate a cost centre")
            .WithDescription("Made against the version last read. Publishes CostCentreChanged with the next version, unless nothing changed.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Created<CostCentreView>> CreateAsync(
        CreateCostCentreRequest request,
        ICommandHandler<CreateCostCentreCommand, CostCentreView> handler,
        CancellationToken cancellationToken)
    {
        CostCentreView created = await handler.HandleAsync(
            new CreateCostCentreCommand(request.Id, request.Code, request.Name, request.ManagerId), cancellationToken);
        return TypedResults.Created($"/cost-centres/{created.Code}", created);
    }

    private static async Task<Ok<Page<CostCentreView>>> ListAsync(
        [FromQuery] string? after,
        [FromQuery, Range(1, 200)] int? limit,
        IQueryHandler<ListCostCentresQuery, Page<CostCentreView>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListCostCentresQuery(after, limit), cancellationToken));

    private static async Task<Ok<CostCentreView>> GetAsync(
        string code, IQueryHandler<GetCostCentreQuery, CostCentreView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetCostCentreQuery(code), cancellationToken));

    private static async Task<Ok<CostCentreView>> ChangeAsync(
        string code,
        ChangeCostCentreRequest request,
        ICommandHandler<ChangeCostCentreCommand, CostCentreView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(
            new ChangeCostCentreCommand(code, request.Version, request.Name, request.ManagerId, request.IsActive),
            cancellationToken));
}
