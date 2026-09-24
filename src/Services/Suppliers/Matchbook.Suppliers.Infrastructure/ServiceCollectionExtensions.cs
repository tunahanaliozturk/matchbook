using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Messaging;
using Matchbook.BuildingBlocks.Persistence;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Suppliers.Application;
using Matchbook.Suppliers.Infrastructure.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Suppliers.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The database, the payment-data key, and the bus. Suppliers consumes nothing, but everything it publishes
    /// goes through the outbox, and the outbox needs the bus to deliver it.
    /// </summary>
    public static IServiceCollection AddSuppliersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFieldProtection(configuration);
        services.AddMatchbookDatabase<SuppliersDbContext, ISuppliersDb>(configuration);
        services.AddMatchbookMessaging<SuppliersDbContext>(configuration, "suppliers", static _ => { });

        // Mapped here, beside the configurations that name the indexes, so the two cannot drift apart.
        services.MapUniqueViolation(
            SupplierIndexes.TaxId, "supplier.tax_id_taken", "Another supplier already has this tax id.");
        services.MapUniqueViolation(
            SupplierIndexes.OnePendingBankAccount,
            "supplier.bank_account_pending",
            "Another bank account is waiting for approval. Approve or reject it first.");
        services.MapUniqueViolation(
            SupplierIndexes.SupplierKey, "request.id_reused", "This id was already used for a different request.");
        services.MapUniqueViolation(
            SupplierIndexes.BankAccountKey, "request.id_reused", "This id was already used for a different request.");

        return services;
    }
}
