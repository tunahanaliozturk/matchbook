using Matchbook.Purchasing.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Purchasing.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers what the handlers need besides the database and the event publisher, which the host provides:
    /// the metrics, and the system clock as the default <see cref="TimeProvider"/> unless the host registered
    /// another first. The handlers themselves are registered by scanning this assembly (ADR 0008).
    /// </summary>
    public static IServiceCollection AddPurchasingApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PurchasingMetrics>();

        return services;
    }
}
