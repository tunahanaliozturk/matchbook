using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Requisitions.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers what the handlers need that is not a handler. The handlers themselves are registered by scanning
    /// (<c>AddHandlersFrom</c> in the host), scoped like the DbContext they share. Infrastructure registers what
    /// they depend on: <see cref="IRequisitionsDb"/>, the event publisher and
    /// <see cref="Common.RequisitionMetrics"/>.
    /// </summary>
    public static IServiceCollection AddRequisitionsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
