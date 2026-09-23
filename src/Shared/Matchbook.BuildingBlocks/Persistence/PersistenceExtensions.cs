using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Matchbook.BuildingBlocks.Persistence;

public static class PersistenceExtensions
{
    /// <summary>
    /// The service's DbContext on Postgres (<c>ConnectionStrings:Database</c>) with snake_case names, exposed to
    /// Application through <typeparamref name="TInterface"/>, migrated before anything else starts, and checked
    /// by the readiness probe.
    /// </summary>
    /// <remarks>
    /// No retrying execution strategy: the outbox wraps every consumer in a transaction, which that strategy
    /// refuses, and a failed message is retried whole by the bus anyway.
    /// </remarks>
    public static IServiceCollection AddMatchbookDatabase<TContext, TInterface>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext, TInterface
        where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");

        services.AddDbContext<TContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<TInterface>(static provider => provider.GetRequiredService<TContext>());
        services.AddHostedService<MigrateOnStart<TContext>>();
        services.AddHealthChecks().AddDbContextCheck<TContext>(tags: [HealthTags.Ready]);

        return services;
    }

    /// <summary>
    /// Applies pending migrations in <c>StartingAsync</c>, which the host runs for every hosted service before any
    /// of them starts, so no consumer ever sees a table that is not there yet. A deployment with several replicas
    /// would run migrations as a separate job; see docs/operations.md.
    /// </summary>
    private sealed class MigrateOnStart<TContext>(IServiceScopeFactory scopes) : IHostedLifecycleService
        where TContext : DbContext
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
