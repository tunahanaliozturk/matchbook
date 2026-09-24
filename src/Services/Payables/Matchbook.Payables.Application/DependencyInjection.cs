using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Application.Features.PaymentRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Payables.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers what the handlers depend on: the matcher, the order race retry, the metrics and the clock. The
    /// handlers themselves are registered by scanning (<c>AddHandlersFrom</c>). The host provides
    /// <see cref="IPayablesDb"/>, the event publisher, the field protector, <see cref="PayerAccount"/> and an
    /// <c>IMeterFactory</c>.
    /// </summary>
    public static IServiceCollection AddPayablesApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PayablesMetrics>();

        services.AddSingleton<OrderRaceRetry>();
        services.AddScoped<InvoiceMatcher>();

        return services;
    }
}
