using System.Diagnostics.Metrics;
using System.Globalization;
using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Application.Common;
using Matchbook.Requisitions.Application.Features.Requisitions;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>
/// The whole path through this service over HTTP and real RabbitMQ: rita raises and submits, Budgets (the
/// probe) reserves, the approvers the amount calls for sign off in turn, and Purchasing (the probe again)
/// receives the approved requisition.
/// </summary>
public sealed class ApprovalRouteTests(RequisitionsFixture fixture) : IClassFixture<RequisitionsFixture>
{
    [Theory]
    [InlineData("9999.99", "mark")]
    [InlineData("10000.00", "mark")]
    [InlineData("10000.01", "mark", "fiona")]
    [InlineData("99999.99", "mark", "fiona")]
    [InlineData("100000.00", "mark", "fiona")]
    [InlineData("100000.01", "mark", "fiona", "carl")]
    public async Task The_amount_decides_who_signs_and_the_last_signature_publishes_the_requisition(string amount, params string[] route)
    {
        decimal total = decimal.Parse(amount, CultureInfo.InvariantCulture);
        Actor[] approvers = [.. route.Select(Person)];

        RequisitionView draft = await fixture.CreateAsync(TestUsers.Rita, total);
        await (await fixture.PostAsync(TestUsers.Rita, draft.Id, "submit")).ReadAsync<RequisitionView>();

        RequisitionSubmitted submitted = await fixture.Probe.WaitForAsync<RequisitionSubmitted>(message => message.RequisitionId == draft.Id);
        submitted.Amount.ShouldBe(total);
        submitted.Number.ShouldBe(draft.Number);
        submitted.CostCentreCode.ShouldBe(RequisitionsFixture.Platform);
        submitted.FiscalYear.ShouldBe(DateTime.UtcNow.Year);

        await fixture.DeliverAsync(new Matchbook.Contracts.Budgets.FundsReserved(
            draft.Id, submitted.CostCentreCode, submitted.FiscalYear, submitted.Amount, DateTimeOffset.UtcNow));

        RequisitionView pending = await fixture.GetAsync(TestUsers.Rita, draft.Id);
        pending.Status.ShouldBe(RequisitionStatus.PendingApproval);
        pending.Steps.Count.ShouldBe(approvers.Length);

        RequisitionView decided = pending;
        foreach (Actor approver in approvers)
        {
            decided.Status.ShouldBe(RequisitionStatus.PendingApproval);
            decided = await (await fixture.PostAsync(approver, draft.Id, "approve")).ReadAsync<RequisitionView>();
        }

        decided.Status.ShouldBe(RequisitionStatus.Approved);

        RequisitionApproved approved = await fixture.Probe.WaitForAsync<RequisitionApproved>(message => message.RequisitionId == draft.Id);
        approved.Amount.ShouldBe(total);
        approved.RequesterId.ShouldBe(TestUsers.Rita.Id);
        approved.SupplierId.ShouldBe(RequisitionsFixture.SupplierId);
        approved.ApproverIds.ShouldBe([.. approvers.Select(static approver => approver.Id)]);
        approved.Lines.Select(static line => (line.LineNumber, line.Description, line.Quantity, line.UnitPrice, line.Amount)).ShouldBe(
        [
            (1, "Laptop", 1m, total - 100m, total - 100m),
            (2, "Laptop bag", 4m, 25m, 100m),
        ]);
    }

    [Fact]
    public async Task Submissions_approvals_and_the_wait_for_approval_are_measured()
    {
        List<(string Instrument, double Value, object? Steps)> measured = [];
        object scope = fixture.Host.Services.GetRequiredService<IMeterFactory>();
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == RequisitionMetrics.MeterName && instrument.Meter.Scope == scope)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();

        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita, 20_000m);
        await (await fixture.PostAsync(TestUsers.Mark, pending.Id, "approve")).ReadAsync<RequisitionView>();
        await (await fixture.PostAsync(TestUsers.Fiona, pending.Id, "approve")).ReadAsync<RequisitionView>();

        lock (measured)
        {
            measured.ShouldContain(measurement => measurement.Instrument == "matchbook.requisitions.submitted" && measurement.Value == 1);
            measured.ShouldContain(measurement => measurement.Instrument == "matchbook.requisitions.approved" && Equals(measurement.Steps, 2));
            measured.ShouldContain(measurement => measurement.Instrument == "matchbook.requisitions.time_to_approval" && measurement.Value >= 0 && Equals(measurement.Steps, 2));
        }

        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            object? steps = null;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "route.steps")
                {
                    steps = tag.Value;
                }
            }

            lock (measured)
            {
                measured.Add((instrument.Name, value, steps));
            }
        }
    }

    private static Actor Person(string name) => name switch
    {
        "mark" => TestUsers.Mark,
        "fiona" => TestUsers.Fiona,
        "carl" => TestUsers.Carl,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}
