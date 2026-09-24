using Matchbook.Payables.Application.Events;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Application.PaymentRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Payables.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the handlers and the metrics. The host provides <see cref="IPayablesDb"/>, the event publisher, the
    /// field protector, <see cref="PayerAccount"/> and an <c>IMeterFactory</c>.
    /// </summary>
    public static IServiceCollection AddPayablesApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PayablesMetrics>();

        services.AddScoped<InvoiceMatcher>();
        services.AddScoped<CaptureInvoiceHandler>();
        services.AddScoped<GetInvoiceHandler>();
        services.AddScoped<ListInvoicesHandler>();
        services.AddScoped<ListInvoiceExceptionsHandler>();
        services.AddScoped<AcceptPriceVarianceHandler>();
        services.AddScoped<ClearSuspectedDuplicateHandler>();

        services.AddScoped<DraftPaymentRunHandler>();
        services.AddScoped<GetPaymentRunHandler>();
        services.AddScoped<ReleasePaymentRunHandler>();
        services.AddScoped<CancelPaymentRunHandler>();
        services.AddScoped<DownloadPaymentFileHandler>();

        services.AddScoped<PurchaseOrderIssuedHandler>();
        services.AddScoped<PurchaseOrderClosedHandler>();
        services.AddScoped<GoodsReceivedHandler>();
        services.AddScoped<SupplierChangedHandler>();

        return services;
    }
}
