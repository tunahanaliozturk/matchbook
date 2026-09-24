using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class SubmissionTests
{
    [Fact]
    public void A_submitted_requisition_waits_for_budgets_in_the_year_it_was_submitted()
    {
        Requisition requisition = A.Draft();

        requisition.Submit(A.Requester, A.CostCentre(), A.Supplier(), A.Now);

        requisition.Status.ShouldBe(RequisitionStatus.Submitted);
        requisition.FiscalYear.ShouldBe(2026);
    }

    [Fact]
    public void The_fiscal_year_is_the_utc_year_of_submission_not_of_the_draft()
    {
        Requisition requisition = A.Draft();
        DateTimeOffset newYearsEveInNewYork = new(2026, 12, 31, 20, 0, 0, TimeSpan.FromHours(-5));

        requisition.Submit(A.Requester, A.CostCentre(), A.Supplier(), newYearsEveInNewYork);

        requisition.Number.ShouldStartWith("REQ-2026-");
        requisition.FiscalYear.ShouldBe(2027);
    }

    [Fact]
    public void Only_the_requester_submits() =>
        A.Refused(
            () => A.Draft().Submit(A.Manager, A.CostCentre(), A.Supplier(), A.Now),
            RequisitionCodes.NotRequester,
            ViolationKind.Forbidden);

    [Fact]
    public void A_requisition_is_submitted_once() =>
        A.Refused(
            () => A.Submitted().Submit(A.Requester, A.CostCentre(), A.Supplier(), A.Now),
            RequisitionCodes.NotDraft,
            ViolationKind.Conflict);

    [Fact]
    public void A_cost_centre_not_known_here_refuses_the_submission() =>
        A.Refused(
            () => A.Draft().Submit(A.Requester, null, A.Supplier(), A.Now),
            RequisitionCodes.CostCentreUnknown,
            ViolationKind.Conflict);

    [Fact]
    public void An_inactive_cost_centre_refuses_the_submission() =>
        A.Refused(
            () => A.Draft().Submit(A.Requester, A.CostCentre(isActive: false), A.Supplier(), A.Now),
            RequisitionCodes.CostCentreInactive,
            ViolationKind.Conflict);

    [Fact]
    public void A_supplier_not_known_here_refuses_the_submission() =>
        A.Refused(
            () => A.Draft().Submit(A.Requester, A.CostCentre(), null, A.Now),
            RequisitionCodes.SupplierUnknown,
            ViolationKind.Conflict);

    [Fact]
    public void An_inactive_supplier_refuses_the_submission() =>
        A.Refused(
            () => A.Draft().Submit(A.Requester, A.CostCentre(), A.Supplier(isActive: false), A.Now),
            RequisitionCodes.SupplierInactive,
            ViolationKind.Conflict);

    [Fact]
    public void The_manager_of_the_cost_centre_cannot_submit_against_it()
    {
        // Nobody else could take the manager step, and the manager may not approve their own requisition.
        Requisition requisition = Requisition.Draft(1, A.Manager, A.Details(), A.Now);

        A.Refused(
            () => requisition.Submit(A.Manager, A.CostCentre(managerId: A.Manager.Id), A.Supplier(), A.Now),
            RequisitionCodes.RequesterIsManager,
            ViolationKind.Conflict);
    }

    [Fact]
    public void A_refused_submission_leaves_the_draft_a_draft()
    {
        Requisition requisition = A.Draft();

        A.Refused(
            () => requisition.Submit(A.Requester, A.CostCentre(), A.Supplier(isActive: false), A.Now),
            RequisitionCodes.SupplierInactive,
            ViolationKind.Conflict);

        requisition.Status.ShouldBe(RequisitionStatus.Draft);
        requisition.FiscalYear.ShouldBeNull();
    }

    [Fact]
    public void Passing_another_cost_centre_is_a_programming_error()
    {
        CostCentre other = new("MKT-GROWTH", 1, "Growth", A.Manager.Id, true);

        Should.Throw<ArgumentException>(() => A.Draft().Submit(A.Requester, other, A.Supplier(), A.Now));
    }

    [Fact]
    public void Passing_another_supplier_is_a_programming_error()
    {
        Supplier other = new(Guid.NewGuid(), 1, "Someone else", true);

        Should.Throw<ArgumentException>(() => A.Draft().Submit(A.Requester, A.CostCentre(), other, A.Now));
    }
}
