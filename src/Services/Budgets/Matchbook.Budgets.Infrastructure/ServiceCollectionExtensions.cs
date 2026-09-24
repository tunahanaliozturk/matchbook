using Matchbook.Budgets.Application;
using Matchbook.Budgets.Infrastructure.Configurations;
using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Messaging;
using Matchbook.BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Budgets.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>The database, the consumers on the bus, and what each unique index means to an HTTP caller.</summary>
    public static IServiceCollection AddBudgetsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMatchbookDatabase<BudgetsDbContext, IBudgetsDb>(configuration);
        services.AddMatchbookMessaging<BudgetsDbContext>(configuration, "budgets", static bus =>
        {
            bus.AddConsumer<RequisitionSubmittedConsumer>();
            bus.AddConsumer<RequisitionRejectedConsumer>();
            bus.AddConsumer<RequisitionCancelledConsumer>();
            bus.AddConsumer<PurchaseOrderCommitmentRequestedConsumer>();
            bus.AddConsumer<InvoiceMatchedConsumer>();
            bus.AddConsumer<PurchaseOrderClosedConsumer>();
        });

        // Only the loser of a race gets these: the handlers look first and refuse with the same codes.
        services.MapUniqueViolation(
            "pk_cost_centres", "cost_centre.already_exists", "A cost centre with this code already exists.");
        services.MapUniqueViolation(
            "ix_budgets_fiscal_year_cost_centre_code",
            "budget.already_exists",
            "This cost centre already has a budget for that year; change its allotment instead.");
        services.MapUniqueViolation(
            IdempotentRequestConfiguration.PrimaryKey,
            "request.in_progress",
            "Another request with this id is being handled. Retry it to get that request's result.");
        return services;
    }
}
