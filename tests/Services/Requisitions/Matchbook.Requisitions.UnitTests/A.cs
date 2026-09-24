using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

/// <summary>Requisitions and people in the states tests start from, with the seeded users' names.</summary>
internal static class A
{
    public const string CostCentreCode = "ENG-PLATFORM";

    public static readonly DateTimeOffset Now = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
    public static readonly Guid SupplierId = Guid.Parse("b0000000-0000-4000-8000-000000000001");

    public static readonly Actor Requester = Person(1, "rita", Roles.Requester);
    public static readonly Actor Manager = Person(2, "mark", Roles.Approver);
    public static readonly Actor OtherManager = Person(3, "maya", Roles.Approver);
    public static readonly Actor Finance = Person(4, "fiona", Roles.FinanceApprover);
    public static readonly Actor Cfo = Person(5, "carl", Roles.Cfo);
    public static readonly Actor OtherFinance = Person(6, "frank", Roles.FinanceApprover);
    public static readonly Actor Buyer = Person(7, "bruno", Roles.Buyer);
    public static readonly Actor Auditor = Person(8, "audrey", Roles.Auditor);

    public static Actor Person(int number, string name, params string[] roles) =>
        new(Guid.Parse($"a0000000-0000-4000-8000-{number:D12}"), name, roles.ToHashSet(StringComparer.Ordinal));

    public static LineInput Line(decimal quantity = 2m, decimal unitPrice = 1_500m, string description = "Laptop") =>
        new(description, quantity, "EA", unitPrice);

    public static RequisitionDetails Details(params LineInput[] lines) =>
        new(CostCentreCode, SupplierId, "Laptops for the two new platform engineers", DateOnly.FromDateTime(Now.UtcDateTime).AddDays(30), lines.Length == 0 ? [Line()] : lines);

    public static RequisitionDetails DetailsWorth(decimal amount) => Details(Line(1m, amount));

    public static CostCentre CostCentre(Guid? managerId = null, bool isActive = true) =>
        new(CostCentreCode, 1, "Platform engineering", managerId ?? Manager.Id, isActive);

    public static Supplier Supplier(bool isActive = true) => new(SupplierId, 1, "Acme Office Supplies BV", isActive);

    public static Requisition Draft(decimal amount = 3_000m) =>
        Requisition.Draft(1, Requester, DetailsWorth(amount), Now);

    public static Requisition Submitted(decimal amount = 3_000m)
    {
        Requisition requisition = Draft(amount);
        requisition.Submit(Requester, CostCentre(), Supplier(), Now);
        return requisition;
    }

    public static Requisition PendingApproval(decimal amount = 3_000m)
    {
        Requisition requisition = Submitted(amount);
        requisition.RecordFundsReserved(Manager.Id, Now);
        return requisition;
    }

    /// <summary>Approved by the manager, then Fiona and Carl where the amount needs them.</summary>
    public static Requisition Approved(decimal amount = 3_000m)
    {
        Requisition requisition = PendingApproval(amount);
        foreach (Actor approver in (Actor[])[Manager, Finance, Cfo])
        {
            if (requisition.Status == RequisitionStatus.PendingApproval)
            {
                requisition.Approve(approver, Now);
            }
        }

        return requisition;
    }

    public static Requisition Ordered()
    {
        Requisition requisition = Approved();
        requisition.RecordPurchaseOrderIssued(Guid.NewGuid(), "PO-2026-000017", Buyer.Id, Now);
        return requisition;
    }

    public static Requisition In(RequisitionStatus status)
    {
        switch (status)
        {
            case RequisitionStatus.Draft:
                return Draft();
            case RequisitionStatus.Submitted:
                return Submitted();
            case RequisitionStatus.PendingApproval:
                return PendingApproval();
            case RequisitionStatus.Approved:
                return Approved();
            case RequisitionStatus.Ordered:
                return Ordered();
            case RequisitionStatus.Closed:
                Requisition closed = Ordered();
                closed.RecordPurchaseOrderClosed(closed.PurchaseOrderId!.Value, "Completed", Now);
                return closed;
            case RequisitionStatus.BudgetRejected:
                Requisition refused = Submitted();
                refused.RecordFundsRefused("insufficient_funds", Now);
                return refused;
            case RequisitionStatus.Rejected:
                Requisition rejected = PendingApproval();
                rejected.Reject(Manager, "Not in this quarter's plan", Now);
                return rejected;
            case RequisitionStatus.Cancelled:
                Requisition cancelled = Submitted();
                cancelled.Cancel(Requester, Now);
                return cancelled;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    /// <summary>Asserts the action is refused with this code and kind, and returns the refusal.</summary>
    public static BusinessRuleException Refused(Action action, string code, ViolationKind kind)
    {
        BusinessRuleException refusal = Should.Throw<BusinessRuleException>(action);
        refusal.Code.ShouldBe(code);
        refusal.Kind.ShouldBe(kind);
        return refusal;
    }
}
