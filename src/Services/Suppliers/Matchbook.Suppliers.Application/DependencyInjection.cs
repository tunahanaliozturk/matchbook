using Matchbook.Suppliers.Application.Common;
using Matchbook.Suppliers.Application.Features.Suppliers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Suppliers.Application;

public static class DependencyInjection
{
    /// <summary>
    /// What the handlers work with. The handlers themselves are registered by scanning (ADR 0008), and the runner
    /// is scoped like the DbContext it works through. <see cref="ISuppliersDb"/>, <c>IEventPublisher</c> and
    /// <c>IFieldProtector</c> are registered by Infrastructure, and the host provides the meter factory.
    /// </summary>
    public static IServiceCollection AddSuppliersApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SupplierMetrics>();
        services.AddScoped<SupplierCommandRunner>();

        return services;
    }
}
