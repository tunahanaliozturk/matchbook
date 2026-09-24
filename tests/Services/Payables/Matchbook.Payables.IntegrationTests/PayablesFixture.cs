using Matchbook.Contracts.Payables;
using Matchbook.Testing;

[assembly: AssemblyFixture(typeof(Matchbook.Testing.Infrastructure))]

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// One Payables host and one probe per test class: the real Program, its own database and virtual host, and a bus
/// standing in for Purchasing and Suppliers that also records what Payables publishes.
/// </summary>
public sealed class PayablesFixture(Matchbook.Testing.Infrastructure infrastructure) : IAsyncLifetime
{
    public const string PayerName = "Matchbook Test GmbH";
    public const string PayerIban = "DE89370400440532013000";
    public const string PayerBic = "COBADEFFXXX";

    public ServiceHost<Program> Host { get; private set; } = null!;

    public EventProbe Probe { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = await ServiceHost.StartAsync<Program>(infrastructure, "payables", new Dictionary<string, string?>
        {
            ["Payer:Name"] = PayerName,
            ["Payer:Iban"] = PayerIban,
            ["Payer:Bic"] = PayerBic,
        });
        Probe = await EventProbe.StartAsync(Host.Broker, static listen => listen.For<InvoiceMatched>().For<InvoicePaid>());
    }

    public async ValueTask DisposeAsync()
    {
        await Probe.DisposeAsync();
        await Host.DisposeAsync();
    }
}
