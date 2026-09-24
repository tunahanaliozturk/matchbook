using System.Net;
using MassTransit.EntityFrameworkCoreIntegration;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Requisitions;
using Matchbook.Contracts.Suppliers;
using Matchbook.Requisitions.Infrastructure;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Matchbook.Testing.Infrastructure))]

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>
/// One Requisitions host per test class, on its own database and virtual host, with a probe standing in for
/// Budgets, Purchasing, Suppliers and anyone listening. The demo realm's cost centres and one active supplier
/// are published through the probe before any test runs, the way the owning services would.
/// </summary>
public sealed class RequisitionsFixture(Matchbook.Testing.Infrastructure infrastructure) : IAsyncLifetime
{
    public const string Platform = "ENG-PLATFORM";
    public const string Growth = "MKT-GROWTH";

    public static readonly Guid SupplierId = Guid.Parse("b0000000-0000-4000-8000-000000000001");

    public ServiceHost<Program> Host { get; private set; } = null!;

    public EventProbe Probe { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = await ServiceHost.StartAsync<Program>(infrastructure, "requisitions");

        // The bus binds its queues after the host starts. Readiness includes the bus, so waiting for it means a
        // message published next is not dropped by an exchange nobody is bound to yet.
        using HttpClient anonymous = Host.CreateClient();
        await Eventually.MatchesAsync(
            async () => (await anonymous.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode,
            static status => status == HttpStatusCode.OK);

        Probe = await EventProbe.StartAsync(Host.Broker, static listen => listen
            .For<RequisitionSubmitted>()
            .For<RequisitionApproved>()
            .For<RequisitionRejected>()
            .For<RequisitionCancelled>());

        await DeliverAsync(new CostCentreChanged(Platform, 1, "Platform engineering", TestUsers.Mark.Id, true, DateTimeOffset.UtcNow));
        await DeliverAsync(new CostCentreChanged(Growth, 1, "Growth marketing", TestUsers.Maya.Id, true, DateTimeOffset.UtcNow));
        await DeliverAsync(new SupplierChanged(SupplierId, 1, "Acme Office Supplies BV", "NL", SupplierStatus.Active, 30, null, DateTimeOffset.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        await Probe.DisposeAsync();
        await Host.DisposeAsync();
    }

    public HttpClient Client(Matchbook.SharedKernel.Actor actor) => Host.ClientFor(actor);

    /// <summary>
    /// Publishes a message as another service would and waits until the consumer has finished with it, which the
    /// inbox records. A test can then assert that nothing changed without guessing how long to wait.
    /// </summary>
    public async Task<Guid> DeliverAsync<T>(T message, Guid? messageId = null)
        where T : class
    {
        Guid id = messageId ?? Guid.NewGuid();
        await Probe.PublishAsync(message, id);
        await Eventually.MatchesAsync(() => ConsumedAsync(id), static consumed => consumed);
        return id;
    }

    /// <summary>A cost centre only this test uses, managed by <paramref name="managerId"/>.</summary>
    public async Task<string> CostCentreManagedByAsync(Guid managerId)
    {
        string code = $"TST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        await DeliverAsync(new CostCentreChanged(code, 1, "Test cost centre", managerId, true, DateTimeOffset.UtcNow));
        return code;
    }

    private Task<bool> ConsumedAsync(Guid messageId) =>
        Host.InScopeAsync(services => services.GetRequiredService<RequisitionsDbContext>()
            .Set<InboxState>()
            .AnyAsync(state => state.MessageId == messageId && state.Consumed != null));
}
