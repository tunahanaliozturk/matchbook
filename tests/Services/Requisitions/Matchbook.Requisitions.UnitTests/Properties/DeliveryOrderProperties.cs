using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests.Properties;

/// <summary>What a consumer sees when messages arrive in any order, some of them more than once.</summary>
public sealed class DeliveryOrderProperties
{
    private static readonly Guid OrderId = Guid.Parse("c0000000-0000-4000-8000-000000000001");

    /// <summary>Versions 1..n of a snapshot, each at least once, some repeated, shuffled.</summary>
    private static readonly Gen<long[]> Versions =
        from count in Gen.Choose(1, 8)
        from repeats in Gen.ListOf(Gen.Choose(1, count))
        from order in Gen.Shuffle(Enumerable.Range(1, count).Concat(repeats).Select(static version => (long)version))
        select order;

    [Property]
    public Property A_cost_centre_copy_ends_at_the_newest_version_whatever_the_order() =>
        Prop.ForAll(Versions.ToArbitrary(), static versions =>
        {
            // As the consumer does it: the first message creates the copy, every later one is applied to it.
            CostCentre? copy = null;
            foreach (long version in versions)
            {
                if (copy is null)
                {
                    copy = new CostCentre(A.CostCentreCode, version, $"v{version}", ManagerOf(version), version % 2 == 0);
                }
                else
                {
                    copy.Apply(version, $"v{version}", ManagerOf(version), version % 2 == 0);
                }
            }

            long newest = versions.Max();
            copy!.Version.ShouldBe(newest);
            copy.Name.ShouldBe($"v{newest}");
            copy.ManagerId.ShouldBe(ManagerOf(newest));
            copy.IsActive.ShouldBe(newest % 2 == 0);
        });

    [Property]
    public Property A_supplier_copy_ends_at_the_newest_version_whatever_the_order() =>
        Prop.ForAll(Versions.ToArbitrary(), static versions =>
        {
            Supplier? copy = null;
            foreach (long version in versions)
            {
                if (copy is null)
                {
                    copy = new Supplier(A.SupplierId, version, $"v{version}", version % 3 != 0);
                }
                else
                {
                    copy.Apply(version, $"v{version}", version % 3 != 0);
                }
            }

            long newest = versions.Max();
            copy!.Version.ShouldBe(newest);
            copy.LegalName.ShouldBe($"v{newest}");
            copy.IsActive.ShouldBe(newest % 3 != 0);
        });

    [Property]
    public Property Issue_and_close_in_any_order_and_number_end_closed_with_the_order_known_once() =>
        Prop.ForAll(PurchaseOrderMessages().ToArbitrary(), static messages =>
        {
            Requisition requisition = A.Approved();

            foreach (bool issued in messages)
            {
                if (issued)
                {
                    requisition.RecordPurchaseOrderIssued(OrderId, "PO-2026-000017", A.Buyer.Id, A.Now);
                }
                else
                {
                    requisition.RecordPurchaseOrderClosed(OrderId, "Completed", A.Now);
                }
            }

            requisition.Status.ShouldBe(RequisitionStatus.Closed);
            requisition.PurchaseOrderId.ShouldBe(OrderId);
            requisition.PurchaseOrderNumber.ShouldBe("PO-2026-000017");
            requisition.Timeline.Count(static entry => entry.Action == TimelineAction.Ordered).ShouldBe(1);
            requisition.Timeline.Count(static entry => entry.Action == TimelineAction.Closed).ShouldBe(1);
        });

    [Property]
    public Property A_cancellation_and_budgets_answer_in_either_order_never_revive_the_requisition() =>
        Prop.ForAll(CancellationRaces().ToArbitrary(), static race =>
        {
            Requisition requisition = A.Submitted(50_000m);
            bool? cancelTold = null;

            foreach (string message in race.Messages)
            {
                switch (message)
                {
                    case "cancel":
                        try
                        {
                            cancelTold = requisition.Cancel(A.Requester, A.Now);
                        }
                        catch (BusinessRuleException refusal) when (refusal.Code == RequisitionCodes.NotCancellable)
                        {
                            cancelTold = null;
                        }

                        break;
                    case "reserved":
                        requisition.RecordFundsReserved(A.Manager.Id, A.Now);
                        break;
                    default:
                        requisition.RecordFundsRefused("insufficient_funds", A.Now);
                        break;
                }
            }

            bool refusalCameFirst = race.Messages.TakeWhile(static message => message != "cancel").Contains("refused");

            if (refusalCameFirst)
            {
                // The requisition had already ended when the requester tried; nothing reserved to release.
                requisition.Status.ShouldBe(RequisitionStatus.BudgetRejected);
                cancelTold.ShouldBeNull();
            }
            else
            {
                // Cancelled from Submitted or PendingApproval: Budgets must be told either way.
                requisition.Status.ShouldBe(RequisitionStatus.Cancelled);
                cancelTold.ShouldBe(true);
                requisition.Steps.ShouldAllBe(static step => step.Decision == ApprovalDecision.Pending);
            }
        });

    private static Guid ManagerOf(long version) => Guid.Parse($"a0000000-0000-4000-8000-{version:D12}");

    private static Gen<bool[]> PurchaseOrderMessages() =>
        from issues in Gen.Choose(1, 3)
        from closes in Gen.Choose(1, 3)
        from order in Gen.Shuffle(Enumerable.Repeat(true, issues).Concat(Enumerable.Repeat(false, closes)))
        select order;

    private static Gen<Race> CancellationRaces() =>
        from answer in Gen.Elements("reserved", "refused")
        from deliveries in Gen.Choose(1, 3)
        from order in Gen.Shuffle(Enumerable.Repeat(answer, deliveries).Append("cancel"))
        select new Race(order);

    public sealed record Race(string[] Messages)
    {
        public override string ToString() => string.Join(", ", Messages);
    }
}
