using System.Diagnostics.Metrics;
using Matchbook.Payables.Domain.Invoices;

namespace Matchbook.Payables.Application;

/// <summary>
/// The business outcomes Payables counts, on the meter <c>Matchbook.Payables</c>. Tags are enum names and trigger
/// names only, so every series stays small however many invoices there are.
/// </summary>
public sealed class PayablesMetrics
{
    public const string MeterName = "Matchbook.Payables";

    private readonly Counter<long> _captured;
    private readonly Counter<long> _evaluated;
    private readonly Counter<long> _rematched;
    private readonly Counter<long> _paid;
    private readonly Histogram<double> _fileSeconds;

    public PayablesMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);

        Meter meter = meters.Create(MeterName);
        _captured = meter.CreateCounter<long>("payables.invoices.captured", "{invoice}", "Invoices captured.");
        _evaluated = meter.CreateCounter<long>(
            "payables.match.evaluations",
            "{invoice}",
            "Three-way match results, by the state the invoice moved to and why.");
        _rematched = meter.CreateCounter<long>(
            "payables.match.rematches",
            "{invoice}",
            "Waiting invoices matched again because an order, a receipt or a closure arrived after them.");
        _paid = meter.CreateCounter<long>("payables.invoices.paid", "{invoice}", "Invoices paid by released payment runs.");
        _fileSeconds = meter.CreateHistogram<double>(
            "payables.payment_file.duration",
            "s",
            "Time to write a pain.001 file, from the first byte to the last.");
    }

    public void Captured() => _captured.Add(1);

    public void Evaluated(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        _evaluated.Add(
            1,
            new KeyValuePair<string, object?>("status", invoice.Status.ToString()),
            new KeyValuePair<string, object?>("reason", invoice.Reason?.ToString()));
    }

    public void Rematched(string trigger, int invoices) =>
        _rematched.Add(invoices, new KeyValuePair<string, object?>("trigger", trigger));

    public void Paid(int invoices) => _paid.Add(invoices);

    public void FileWritten(TimeSpan elapsed, int transactions) =>
        _fileSeconds.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("size", SizeBucket(transactions)));

    // The histogram's own buckets are time; a coarse size tag keeps a 50-invoice run from hiding a slow 50,000 one.
    private static string SizeBucket(int transactions) => transactions switch
    {
        <= 100 => "le_100",
        <= 1_000 => "le_1000",
        <= 10_000 => "le_10000",
        _ => "gt_10000",
    };
}
