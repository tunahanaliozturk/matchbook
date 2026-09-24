using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Messaging;
using Matchbook.BuildingBlocks.Persistence;
using Matchbook.Purchasing.Application;
using Matchbook.Purchasing.Infrastructure.Messaging;
using Matchbook.Purchasing.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Purchasing.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string ServiceName = "purchasing";

    /// <summary>
    /// Everything Purchasing needs below the HTTP layer: the handlers, the database (migrated on start), and the
    /// bus with one queue per consumed event, each behind the outbox and inbox.
    /// </summary>
    public static IServiceCollection AddPurchasingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPurchasingApplication();
        services.AddMatchbookDatabase<PurchasingDbContext, IPurchasingDb>(configuration);
        services.AddMatchbookMessaging<PurchasingDbContext>(configuration, ServiceName, static bus =>
        {
            bus.AddConsumer<RequisitionApprovedConsumer>();
            bus.AddConsumer<FundsCommittedConsumer>();
            bus.AddConsumer<FundsCommitmentRejectedConsumer>();
            bus.AddConsumer<SupplierChangedConsumer>();
            bus.AddConsumer<InvoiceMatchedConsumer>();
        });

        // Two identical receipt requests racing each other: the loser trips the primary key. It is the same
        // situation as losing on the order's row version, so it gets the same answer, and a retry finds the
        // receipt the winner recorded.
        services.MapUniqueViolation(
            "pk_goods_receipts",
            "concurrency.conflict",
            "The same receipt was being recorded by another request. Retry to get it.");

        return services;
    }
}
