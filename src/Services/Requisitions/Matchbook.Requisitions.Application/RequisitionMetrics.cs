using System.Diagnostics.Metrics;
using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.Application;

/// <summary>
/// The outcomes worth watching, recorded by the handlers after the change that caused them commits, so a lost
/// race or a failed save is never counted.
/// </summary>
/// <remarks>
/// The meter comes from the host's <c>IMeterFactory</c>, which lives in a package this layer may not reference;
/// the instruments only need <see cref="Meter"/> itself.
/// </remarks>
public sealed class RequisitionMetrics
{
    public const string MeterName = "Matchbook.Requisitions";

    // Approvals wait on people, so the buckets run from a minute to a fortnight rather than the default
    // millisecond-shaped ones.
    private static readonly double[] ApprovalBuckets = [60, 300, 900, 3_600, 14_400, 28_800, 86_400, 172_800, 432_000, 604_800, 1_209_600];

    private readonly Counter<long> _submitted;
    private readonly Counter<long> _approved;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _budgetRejected;
    private readonly Histogram<double> _timeToApproval;

    public RequisitionMetrics(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);

        _submitted = meter.CreateCounter<long>(
            "matchbook.requisitions.submitted", "{requisition}", "Requisitions sent to Budgets for a reservation.");
        _approved = meter.CreateCounter<long>(
            "matchbook.requisitions.approved", "{requisition}", "Requisitions whose last approval step was signed off.");
        _rejected = meter.CreateCounter<long>(
            "matchbook.requisitions.rejected", "{requisition}", "Requisitions an approver rejected.");
        _budgetRejected = meter.CreateCounter<long>(
            "matchbook.requisitions.budget_rejected", "{requisition}", "Requisitions Budgets would not reserve funds for.");
        _timeToApproval = meter.CreateHistogram(
            "matchbook.requisitions.time_to_approval",
            "s",
            "Time from submission to the last approval.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = ApprovalBuckets });
    }

    internal void Submitted() => _submitted.Add(1);

    internal void Rejected() => _rejected.Add(1);

    internal void BudgetRejected(string reason) =>
        _budgetRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Counts an approved requisition and how long it waited, tagged by the length of its route.</summary>
    internal void Approved(Requisition requisition)
    {
        KeyValuePair<string, object?> steps = new("route.steps", requisition.Steps.Count);
        DateTimeOffset submitted = requisition.Timeline.Single(static entry => entry.Action == TimelineAction.Submitted).At;
        DateTimeOffset approved = requisition.Steps.Max(static step => step.DecidedAt) ?? submitted;

        _approved.Add(1, steps);
        _timeToApproval.Record((approved - submitted).TotalSeconds, steps);
    }
}
