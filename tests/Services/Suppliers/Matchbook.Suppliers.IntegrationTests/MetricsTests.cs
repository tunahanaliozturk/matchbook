using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Application;
using Matchbook.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Suppliers.IntegrationTests;

[Collection(SharedHost.Name)]
public sealed class MetricsTests(SuppliersFixture fixture)
{
    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task Saved_changes_and_refusals_are_counted_on_the_suppliers_meter()
    {
        using var listener = new MeasurementListener(fixture.Host.Services.GetRequiredService<IMeterFactory>());
        Actor both = TestUsers.Stranger(Roles.SupplierAdmin, Roles.SupplierApprover);

        Guid id = (await _api.CreateAsync(both)).Id;
        Guid account = SupplierApi.PendingAccount(await _api.ProposeAsync(both, id, SupplierApi.NewIban()));
        (await _api.PostAsync(both, $"/suppliers/{id}/bank-accounts/{account}/approve")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await _api.DoAsync(TestUsers.Sofia, id, $"bank-accounts/{account}/approve");

        listener.Measurements.ShouldContain(("matchbook.suppliers.changes", "change", "created"));
        listener.Measurements.ShouldContain(("matchbook.suppliers.changes", "change", "bank_account_proposed"));
        listener.Measurements.ShouldContain(("matchbook.suppliers.changes", "change", "bank_account_approved"));
        listener.Measurements.ShouldContain(("matchbook.suppliers.refusals", "code", "supplier.self_approval"));
    }

    /// <summary>
    /// Listens only to meters made by this host's factory, so another host in the same process, or another test
    /// assembly's meter of the same name, cannot add measurements.
    /// </summary>
    private sealed class MeasurementListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, string Tag, string Value)> _measurements = new();

        public MeasurementListener(IMeterFactory scope)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SupplierMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, scope))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            {
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    _measurements.Enqueue((instrument.Name, tag.Key, tag.Value?.ToString() ?? ""));
                }
            });
            _listener.Start();
        }

        public IReadOnlyCollection<(string Instrument, string Tag, string Value)> Measurements => [.. _measurements];

        public void Dispose() => _listener.Dispose();
    }
}
