using System.Diagnostics.Metrics;
using Matchbook.Budgets.Application.IntegrationEvents;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.Application.Common;

/// <summary>
/// The business outcomes worth a graph: how many reservations and commitments were granted or refused and why,
/// how often an invoice pushes a budget past its allotment, and how long a grant waits for its budget's row lock,
/// which is the first number to rise when one budget becomes a hot spot.
/// </summary>
/// <remarks>
/// A static meter rather than one from IMeterFactory: the factory lives in a package the application layer is not
/// allowed to reference, and OpenTelemetry finds a meter by its name either way.
/// </remarks>
internal static class BudgetsMetrics
{
    public const string MeterName = "Matchbook.Budgets";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Reservations = Meter.CreateCounter<long>(
        "matchbook.budgets.reservations", description: "Reservations decided, by outcome and refusal reason.");

    private static readonly Counter<long> Commitments = Meter.CreateCounter<long>(
        "matchbook.budgets.commitments", description: "Commitment attempts decided, by outcome and refusal reason.");

    private static readonly Counter<long> Overspends = Meter.CreateCounter<long>(
        "matchbook.budgets.overspends", description: "Invoices that took a budget past its allotment.");

    private static readonly Histogram<double> GrantDuration = Meter.CreateHistogram<double>(
        "matchbook.budgets.grant.duration",
        unit: "s",
        description: "One conditional grant UPDATE, including the wait for the budget's row lock.");

    public static void ReservationGranted() => Reservations.Add(1, Granted);

    public static void ReservationRefused(FundsRefusal reason) => Reservations.Add(1, Refused, Reason(reason));

    public static void CommitmentGranted() => Commitments.Add(1, Granted);

    public static void CommitmentRefused(FundsRefusal reason) => Commitments.Add(1, Refused, Reason(reason));

    public static void Overspent() => Overspends.Add(1);

    public static void GrantTook(TimeSpan elapsed, string grant, bool granted) =>
        GrantDuration.Record(elapsed.TotalSeconds, new("grant", grant), granted ? Granted : Refused);

    private static KeyValuePair<string, object?> Granted => new("outcome", "granted");

    private static KeyValuePair<string, object?> Refused => new("outcome", "refused");

    private static KeyValuePair<string, object?> Reason(FundsRefusal reason) => new("reason", reason.ToReason());
}
