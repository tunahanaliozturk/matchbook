using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.Purchasing.Application;
using Matchbook.Purchasing.Application.PurchaseOrders;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

public sealed class MetricsTests(PurchasingFixture fixture) : IClassFixture<PurchasingFixture>
{
    [Fact]
    public async Task The_meter_counts_drafts_refusals_issues_receipts_and_closes_by_reason_and_times_the_issue()
    {
        ConcurrentQueue<(string Instrument, double Value, string? Reason)> measured = [];
        using var listener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == PurchasingMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measured.Enqueue((instrument.Name, value, Reason(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measured.Enqueue((instrument.Name, value, Reason(tags))));
        listener.Start();

        PurchaseOrderView draft = await fixture.DraftAsync(await fixture.ActiveSupplierAsync(), (4m, 10m));
        PurchaseOrderCommitmentRequested first = await fixture.IssueAsync(draft.Id, attempt: 1);
        await fixture.PublishAndWaitAsync(Messages.Rejected(first, FundsRejectionReason.InsufficientFunds));
        PurchaseOrderCommitmentRequested second = await fixture.IssueAsync(draft.Id, attempt: 2);
        await fixture.PublishAndWaitAsync(Messages.Committed(second));
        (await fixture.ClientFor(TestUsers.Rosa).PostJsonAsync(Routes.Receipts(draft.Id), new { lines = new[] { new { lineNumber = 1, quantity = 1m } } }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await fixture.ClientFor(TestUsers.Bruno).PostAsync(Routes.ShortClose(draft.Id), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        listener.RecordObservableInstruments();
        measured.ShouldContain(static m => m.Instrument == "purchasing.orders.drafted" && m.Value == 1);
        measured.ShouldContain(static m => m.Instrument == "purchasing.commitments.rejected" && m.Reason == FundsRejectionReason.InsufficientFunds);
        measured.ShouldContain(static m => m.Instrument == "purchasing.orders.issued" && m.Value == 1);
        measured.ShouldContain(static m => m.Instrument == "purchasing.orders.time_to_issue" && m.Value > 0);
        measured.ShouldContain(static m => m.Instrument == "purchasing.receipts.recorded" && m.Value == 1);
        measured.ShouldContain(static m => m.Instrument == "purchasing.orders.closed" && m.Reason == PurchaseOrderCloseReason.ShortClosed);
    }

    private static string? Reason(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "reason")
            {
                return tag.Value as string;
            }
        }

        return null;
    }
}
