using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Messaging;
using Matchbook.BuildingBlocks.Persistence;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Payables.Application;
using Matchbook.Payables.Infrastructure.Configurations;
using Matchbook.Payables.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Payables.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The database, the bus with its four consumers, the payment-data key, and what each unique index means to a
    /// client.
    /// </summary>
    public static IServiceCollection AddPayablesInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFieldProtection(configuration);
        services.AddMatchbookDatabase<PayablesDbContext, IPayablesDb>(configuration);
        services.AddMatchbookMessaging<PayablesDbContext>(configuration, "payables", static bus =>
        {
            bus.AddConsumer<PurchaseOrderIssuedConsumer>();
            bus.AddConsumer<PurchaseOrderClosedConsumer>();
            bus.AddConsumer<GoodsReceivedConsumer>();
            bus.AddConsumer<SupplierChangedConsumer>();
        });

        services.MapUniqueViolation(
            InvoiceConfiguration.UniqueNumberIndex,
            "invoice.duplicate",
            "The supplier already has an invoice with this number.");
        services.MapUniqueViolation(
            PaymentRunItemConfiguration.ActiveInvoiceIndex,
            "payment_run.invoice_taken",
            "Some of these invoices were taken by another payment run at the same moment. Draft again.");

        // Two writers creating the same row at once: the first mention of an order, a supplier or a receipt, or two
        // requests carrying one client id. The loser should retry, which is what concurrency.conflict tells it.
        foreach (string key in (string[])["pk_purchase_orders", "pk_suppliers", "pk_receipts", "pk_invoices", "pk_payment_runs"])
        {
            services.MapUniqueViolation(key, "concurrency.conflict", "Another request created the same record at the same moment. Retry.");
        }

        return services;
    }
}
