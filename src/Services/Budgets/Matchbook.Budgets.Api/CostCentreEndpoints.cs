using System.ComponentModel.DataAnnotations;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Application.CostCentres;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Matchbook.Budgets.Api;

internal static class CostCentreEndpoints
{
    public static IEndpointRouteBuilder MapCostCentres(this IEndpointRouteBuilder app)
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
        CreateCostCentreRequest request, CreateCostCentreHandler handler, CancellationToken cancellationToken)
    {
        CostCentreView created = await handler.HandleAsync(
            new CreateCostCentre(request.Id, request.Code, request.Name, request.ManagerId), cancellationToken);
        return TypedResults.Created($"/cost-centres/{created.Code}", created);
    }

    private static async Task<Ok<Page<CostCentreView>>> ListAsync(
        [FromQuery] string? after,
        [FromQuery, Range(1, 200)] int? limit,
        ListCostCentresHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListCostCentres(after, limit), cancellationToken));

    private static async Task<Ok<CostCentreView>> GetAsync(
        string code, GetCostCentreHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(code, cancellationToken));

    private static async Task<Ok<CostCentreView>> ChangeAsync(
        string code, ChangeCostCentreRequest request, ChangeCostCentreHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(
            new ChangeCostCentre(code, request.Version, request.Name, request.ManagerId, request.IsActive),
            cancellationToken));
}
