using System.Net.Http.Headers;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Testing;

/// <summary>
/// A service's real host, started against its own database and RabbitMQ virtual host in the shared containers,
/// trusting tokens from <see cref="TestIdentity"/>. Everything else, the outbox, the inbox, the consumers, the
/// migrations and the authorization policies, is the production wiring.
/// </summary>
/// <typeparam name="TEntryPoint">Any type in the service's Api assembly, usually <c>Program</c>.</typeparam>
public sealed class ServiceHost<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly Dictionary<string, string?> _settings;

    internal ServiceHost(Dictionary<string, string?> settings) => _settings = settings;

    /// <summary>The broker address this host uses, for an <see cref="EventProbe"/> to join.</summary>
    public Uri Broker { get; internal init; } = null!;

    /// <summary>The connection string of this host's own database.</summary>
    public string Database { get; internal init; } = "";

    /// <summary>An HTTP client that calls the service as <paramref name="actor"/>.</summary>
    public HttpClient ClientFor(Actor actor)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestIdentity.TokenFor(actor));
        return client;
    }

    /// <summary>Runs <paramref name="work"/> in a fresh DI scope, for reading the service's database directly.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        foreach ((string key, string? value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(static services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, static options =>
            {
                options.TokenValidationParameters.ValidIssuer = TestIdentity.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = TestIdentity.SigningKey;
            }));
    }
}

/// <summary>Starts a <see cref="ServiceHost{TEntryPoint}"/>.</summary>
public static class ServiceHost
{
    /// <summary>
    /// Creates a database and a virtual host, then starts the service against them, migrations and bus included.
    /// </summary>
    /// <param name="settings">Extra configuration for the service, such as its payer account.</param>
    public static async Task<ServiceHost<TEntryPoint>> StartAsync<TEntryPoint>(
        Infrastructure infrastructure,
        string service,
        IReadOnlyDictionary<string, string?>? settings = null)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(infrastructure);

        string database = await infrastructure.CreateDatabaseAsync(service);
        Uri broker = await infrastructure.CreateVirtualHostAsync(service);

        Dictionary<string, string?> all = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Database"] = database,
            ["ConnectionStrings:RabbitMq"] = broker.ToString(),
            ["Auth:Authority"] = "",
            ["Auth:Audience"] = TestIdentity.Audience,
            ["Encryption:ActiveKey"] = "test",
            ["Encryption:Keys:test"] = Convert.ToBase64String(new byte[32]),
            ["Logging:LogLevel:Default"] = "Warning",
        };

        foreach ((string key, string? value) in settings ?? new Dictionary<string, string?>())
        {
            all[key] = value;
        }

        var host = new ServiceHost<TEntryPoint>(all) { Broker = broker, Database = database };

        // Building the server runs StartingAsync, so migrations are applied on return. The bus is only starting:
        // until each receive endpoint has declared its queue and bindings, an event a test publishes to it is dropped
        // by the broker for want of a binding, and the test times out waiting for it. Readiness covers the bus.
        _ = host.Server;
        using HttpClient client = host.CreateClient();
        await Eventually.MatchesAsync(() => ReadyAsync(client), static ready => ready);
        return host;
    }

    private static async Task<bool> ReadyAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        return response.IsSuccessStatusCode;
    }
}
