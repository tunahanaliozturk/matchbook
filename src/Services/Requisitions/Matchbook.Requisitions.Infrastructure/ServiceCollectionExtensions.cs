using System.Diagnostics.Metrics;
using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Messaging;
using Matchbook.BuildingBlocks.Persistence;
using Matchbook.Requisitions.Application;
using Matchbook.Requisitions.Domain;
using Matchbook.Requisitions.Infrastructure.Configurations;
using Matchbook.Requisitions.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Requisitions.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The database (migrated on start), RabbitMQ with the outbox and inbox and the six consumers, the meter,
    /// and what the service's unique index means to a client.
    /// </summary>
    public static IServiceCollection AddRequisitionsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMatchbookDatabase<RequisitionsDbContext, IRequisitionsDb>(configuration);
        services.AddMatchbookMessaging<RequisitionsDbContext>(configuration, "requisitions", static bus =>
        {
            bus.AddConsumer<CostCentreChangedConsumer>();
            bus.AddConsumer<SupplierChangedConsumer>();
            bus.AddConsumer<FundsReservedConsumer>();
            bus.AddConsumer<FundsReservationRejectedConsumer>();
            bus.AddConsumer<PurchaseOrderIssuedConsumer>();
            bus.AddConsumer<PurchaseOrderClosedConsumer>();
        });

        // The domain refuses a second decision by one person first, with a 403. The index only speaks when two
        // saves race past that check, and then the loser hears the same code.
        services.MapUniqueViolation(
            ApprovalStepConfiguration.OneDecisionPerPerson,
            RequisitionCodes.DuplicateApprover,
            "You already decided a step of this requisition; the next one needs someone else.");

        services.AddSingleton(static provider =>
            new RequisitionMetrics(provider.GetRequiredService<IMeterFactory>().Create(RequisitionMetrics.MeterName)));

        return services;
    }
}
