using Matchbook.Budgets.Application.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Budgets.Application;

public static class DependencyInjection
{
    /// <summary>
    /// What the handlers use that is not itself a handler. The handlers are registered by scanning
    /// (<c>AddHandlersFrom</c>), scoped like the DbContext they share. <see cref="IBudgetsDb"/> and
    /// <c>IEventPublisher</c> come from Infrastructure and the messaging setup.
    /// </summary>
    public static IServiceCollection AddBudgetsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ReservationReleaser>();
        return services;
    }
}
