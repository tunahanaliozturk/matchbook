using Matchbook.Purchasing.Application.IncomingEvents;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Purchasing.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers every handler. The host provides <see cref="IPurchasingDb"/> and the event publisher; the
    /// system clock is the default <see cref="TimeProvider"/> unless the host registered another first.
    /// </summary>
    public static IServiceCollection AddPurchasingApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<GetPurchaseOrderHandler>();
        services.AddScoped<ListPurchaseOrdersHandler>();
        services.AddScoped<AmendDraftLineHandler>();
        services.AddScoped<IssuePurchaseOrderHandler>();
        services.AddScoped<RecordReceiptHandler>();
        services.AddScoped<ShortClosePurchaseOrderHandler>();
        services.AddScoped<CancelPurchaseOrderHandler>();

        services.AddScoped<RequisitionApprovedHandler>();
        services.AddScoped<FundsCommittedHandler>();
        services.AddScoped<FundsCommitmentRejectedHandler>();
        services.AddScoped<SupplierChangedHandler>();
        services.AddScoped<InvoiceMatchedHandler>();

        return services;
    }
}
