using System.Diagnostics.Metrics;

namespace Matchbook.Suppliers.Application;

/// <summary>
/// The service's business outcomes on the <c>Matchbook.Suppliers</c> meter. A rise in approved bank account
/// changes, or in refusals coded <c>supplier.self_approval</c>, is where a fraud review starts.
/// </summary>
/// <remarks>
/// Here rather than in Infrastructure because the handlers are where an outcome is known, and reaching them from
/// Infrastructure would take an interface with one implementation. <see cref="IMeterFactory"/> is part of the
/// base library, so this names no technology.
/// </remarks>
public sealed class SupplierMetrics
{
    public const string MeterName = "Matchbook.Suppliers";

    private readonly Counter<long> _changes;
    private readonly Counter<long> _refusals;

    public SupplierMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);

        // The factory owns the meter and disposes it with the container.
        Meter meter = meters.Create(MeterName);
        _changes = meter.CreateCounter<long>(
            "matchbook.suppliers.changes", "{change}", "Changes saved to a supplier, tagged with the kind of change.");
        _refusals = meter.CreateCounter<long>(
            "matchbook.suppliers.refusals", "{refusal}", "Commands a rule or a lost race refused, tagged with the code.");
    }

    public void Changed(string change) => _changes.Add(1, new KeyValuePair<string, object?>("change", change));

    public void Refused(string code) => _refusals.Add(1, new KeyValuePair<string, object?>("code", code));
}
