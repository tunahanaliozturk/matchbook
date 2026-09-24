using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Suppliers.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The handlers, scoped like the DbContext they work through. <see cref="ISuppliersDb"/>,
    /// <c>IEventPublisher</c> and <c>IFieldProtector</c> are registered by Infrastructure, and the host provides
    /// the meter factory.
    /// </summary>
    public static IServiceCollection AddSuppliersApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SupplierMetrics>();
        services.AddScoped<SupplierCommandRunner>();

        services.AddScoped<CreateSupplierHandler>();
        services.AddScoped<ChangeSupplierDetailsHandler>();
        services.AddScoped<SubmitSupplierHandler>();
        services.AddScoped<ActivateSupplierHandler>();
        services.AddScoped<BlockSupplierHandler>();
        services.AddScoped<UnblockSupplierHandler>();
        services.AddScoped<ProposeBankAccountHandler>();
        services.AddScoped<ApproveBankAccountHandler>();
        services.AddScoped<RejectBankAccountHandler>();
        services.AddScoped<GetSupplierHandler>();
        services.AddScoped<ListSuppliersHandler>();

        return services;
    }
}
