using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class TimelineTests
{
    [Fact]
    public void Every_change_adds_one_entry_numbered_by_the_revision()
    {
        DateTimeOffset reserved = A.Now.AddMinutes(1);
        DateTimeOffset issued = A.Now.AddDays(2);
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.DetailsWorth(50_000m), A.Now);
        requisition.Edit(A.Requester, A.DetailsWorth(60_000m), A.Now);
        requisition.Submit(A.Requester, A.CostCentre(), A.Supplier(), A.Now);
        requisition.RecordFundsReserved(A.Manager.Id, reserved);
        requisition.Approve(A.Manager, A.Now);
        requisition.Approve(A.Finance, A.Now);
        requisition.RecordPurchaseOrderIssued(Guid.NewGuid(), "PO-2026-000017", A.Buyer.Id, issued);
        requisition.RecordPurchaseOrderClosed(Guid.NewGuid(), "Completed", issued);

        requisition.Timeline.Select(entry => (entry.Sequence, entry.Action, entry.ActorId)).ShouldBe(
        [
            (1, TimelineAction.Created, A.Requester.Id),
            (2, TimelineAction.Edited, A.Requester.Id),
            (3, TimelineAction.Submitted, A.Requester.Id),
            (4, TimelineAction.FundsReserved, null),
            (5, TimelineAction.StepApproved, A.Manager.Id),
            (6, TimelineAction.Approved, A.Finance.Id),
            (7, TimelineAction.Ordered, A.Buyer.Id),
            (8, TimelineAction.Closed, null),
        ]);
        requisition.Revision.ShouldBe(8);
    }

    [Fact]
    public void A_command_records_who_acted_by_id_and_name_and_when()
    {
        Requisition requisition = A.PendingApproval();
        DateTimeOffset decided = A.Now.AddHours(3);

        requisition.Reject(A.Manager, "Wait for the new budget", decided);

        TimelineEntry entry = requisition.Timeline[^1];
        entry.Action.ShouldBe(TimelineAction.Rejected);
        entry.ActorId.ShouldBe(A.Manager.Id);
        entry.ActorName.ShouldBe("mark");
        entry.At.ShouldBe(decided);
        entry.Detail.ShouldBe("Wait for the new budget");
    }

    [Fact]
    public void A_fact_from_another_service_is_recorded_when_it_happened_there()
    {
        Requisition requisition = A.Submitted(150_000m);
        DateTimeOffset reservedAt = A.Now.AddSeconds(-30);

        requisition.RecordFundsReserved(A.Manager.Id, reservedAt);

        TimelineEntry entry = requisition.Timeline[^1];
        entry.At.ShouldBe(reservedAt);
        entry.ActorName.ShouldBeNull();
        entry.Detail.ShouldBe("Route: Manager, Finance, Cfo");
    }

    [Fact]
    public void A_refused_command_leaves_no_trace()
    {
        Requisition requisition = A.PendingApproval();
        int entries = requisition.Timeline.Count;

        Should.Throw<BusinessRuleException>(() => requisition.Approve(A.Requester, A.Now));

        requisition.Timeline.Count.ShouldBe(entries);
    }
}
