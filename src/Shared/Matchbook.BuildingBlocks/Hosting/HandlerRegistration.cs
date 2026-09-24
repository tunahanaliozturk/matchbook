using System.Reflection;
using Matchbook.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.BuildingBlocks.Hosting;

public static class HandlerRegistration
{
    private static readonly Type[] HandlerInterfaces =
        [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IIntegrationEventHandler<>)];

    /// <summary>
    /// Registers every command, query and integration event handler in <paramref name="assembly"/>, scoped, under
    /// its handler interface and under its own type. The two resolve to one instance per scope, so code that needs
    /// the concrete handler (a retry that resolves it again in a fresh scope) and code that asks by interface agree.
    /// </summary>
    /// <remarks>
    /// Scanning replaces a hand-kept list of registrations that a new handler could be left out of. The price is
    /// that a handler is registered by being in the assembly, which the architecture tests make safe by holding
    /// every handler to its folder and its name (ADR 0008).
    /// </remarks>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (Type type in assembly.GetTypes().Where(static type => type is { IsClass: true, IsAbstract: false }))
        {
            Type[] contracts =
            [
                .. type.GetInterfaces().Where(static contract =>
                    contract.IsGenericType && HandlerInterfaces.Contains(contract.GetGenericTypeDefinition())),
            ];

            if (contracts.Length == 0)
            {
                continue;
            }

            services.TryAddScoped(type);

            foreach (Type contract in contracts)
            {
                services.TryAddScoped(contract, provider => provider.GetRequiredService(type));
            }
        }

        return services;
    }
}
