using System.Diagnostics.Metrics;
using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.Application.Common;

/// <summary>
/// Purchasing's business outcomes as OpenTelemetry instruments on the <c>Matchbook.Purchasing</c> meter. Each is
/// recorded after the change it counts was saved, so a refused request is never counted.
/// </summary>
/// <remarks>
/// Inside a consumer the save is not yet the commit: the outbox commits after the consumer returns. A message
/// whose commit fails is retried and counted again. That is rare enough to leave, and a counter that is slightly
/// high on a bad day is still the right shape on a dashboard.
/// </remarks>
public sealed class PurchasingMetrics : IDisposable
{
    public const string MeterName = "Matchbook.Purchasing";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _drafted;
    private readonly Counter<long> _issued;
    private readonly Counter<long> _commitmentsRejected;
    private readonly Counter<long> _receipts;
    private readonly Counter<long> _closed;
    private readonly Histogram<double> _timeToIssue;

    public PurchasingMetrics()
    {
        _drafted = _meter.CreateCounter<long>("matchbook.purchasing.orders.drafted", "{order}", "Orders drafted from approved requisitions.");
        _issued = _meter.CreateCounter<long>("matchbook.purchasing.orders.issued", "{order}", "Orders issued after Budgets committed the funds.");
        _commitmentsRejected = _meter.CreateCounter<long>(
            "matchbook.purchasing.commitments.rejected", "{request}", "Commitment requests Budgets refused, by reason.");
        _receipts = _meter.CreateCounter<long>("matchbook.purchasing.receipts.recorded", "{receipt}", "Goods receipts recorded.");
        _closed = _meter.CreateCounter<long>("matchbook.purchasing.orders.closed", "{order}", "Orders closed, by reason.");
        _timeToIssue = _meter.CreateHistogram<double>(
            "matchbook.purchasing.orders.time_to_issue",
            "s",
            "From the draft, made when the requisition's approval is consumed, to the order being issued.",
            advice: new InstrumentAdvice<double>
            {
                // Hours and days are the interesting range: buyers act on approvals, and Budgets answers in
                // milliseconds, so the default millisecond-scale buckets would put everything in the last one.
                HistogramBucketBoundaries = [60, 300, 900, 3_600, 14_400, 28_800, 86_400, 259_200, 604_800],
            });
    }

    public void OrderDrafted() => _drafted.Add(1);

    public void OrderIssued(PurchaseOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        _issued.Add(1);

        if (order.IssuedAt is { } issuedAt)
        {
            _timeToIssue.Record((issuedAt - order.DraftedAt).TotalSeconds);
        }
    }

    public void CommitmentRejected(string reason) => _commitmentsRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void ReceiptRecorded() => _receipts.Add(1);

    public void OrderClosed(PurchaseOrderStatus status) =>
        _closed.Add(1, new KeyValuePair<string, object?>("reason", status.ToString()));

    public void Dispose() => _meter.Dispose();
}
