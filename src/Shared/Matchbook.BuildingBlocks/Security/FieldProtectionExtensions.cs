using Matchbook.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.BuildingBlocks.Security;

public static class FieldProtectionExtensions
{
    /// <summary>
    /// Registers the protector built from <c>Encryption:*</c>, as itself for EF value converters and as
    /// <see cref="IFieldProtector"/> for the inner layers. Fails at startup, not at first use, when a key is missing
    /// or the wrong length.
    /// </summary>
    public static IServiceCollection AddFieldProtection(this IServiceCollection services, IConfiguration configuration)
    {
        ColumnProtector protector = ColumnProtector.FromConfiguration(configuration);
        services.AddSingleton(protector);
        services.AddSingleton<IFieldProtector>(protector);
        return services;
    }
}
