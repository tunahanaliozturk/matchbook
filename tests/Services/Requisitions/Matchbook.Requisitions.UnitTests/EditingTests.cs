using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class EditingTests
{
    [Fact]
    public void The_requester_replaces_what_a_draft_says()
    {
        Requisition requisition = A.Draft();
        Guid otherSupplier = Guid.NewGuid();

        requisition.Edit(
            A.Requester,
            A.Details(A.Line(1m, 100m), A.Line(2m, 50m)) with { SupplierId = otherSupplier, CostCentreCode = "MKT-GROWTH" },
            A.Now);

        requisition.SupplierId.ShouldBe(otherSupplier);
        requisition.CostCentreCode.ShouldBe("MKT-GROWTH");
        requisition.Lines.Count.ShouldBe(2);
        requisition.Amount.ShouldBe(200m);
    }

    [Fact]
    public void Dropping_lines_keeps_the_numbers_from_one()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(),
            1, A.Requester, A.Details(A.Line(description: "a"), A.Line(description: "b"), A.Line(description: "c")), A.Now);

        requisition.Edit(A.Requester, A.Details(A.Line(description: "c")), A.Now);

        requisition.Lines.Select(line => (line.LineNumber, line.Description)).ShouldBe([(1, "c")]);
    }

    [Fact]
    public void Someone_other_than_the_requester_may_not_edit() =>
        A.Refused(() => A.Draft().Edit(A.Manager, A.Details(), A.Now), RequisitionCodes.NotRequester, ViolationKind.Forbidden);

    [Theory]
    [InlineData(RequisitionStatus.Submitted)]
    [InlineData(RequisitionStatus.PendingApproval)]
    [InlineData(RequisitionStatus.Approved)]
    [InlineData(RequisitionStatus.Cancelled)]
    public void Only_a_draft_can_be_edited(RequisitionStatus status) =>
        A.Refused(() => A.In(status).Edit(A.Requester, A.Details(), A.Now), RequisitionCodes.NotDraft, ViolationKind.Conflict);

    [Fact]
    public void A_refused_edit_changes_nothing()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(A.Line(1m, 10m), A.Line(1m, 20m)), A.Now);
        int revision = requisition.Revision;

        A.Refused(
            () => requisition.Edit(A.Requester, A.Details(A.Line(5m, 5m), A.Line(quantity: -1m)), A.Now),
            RequisitionCodes.LineInvalid,
            ViolationKind.Invalid);

        requisition.Lines.Select(line => line.Amount).ShouldBe([10m, 20m]);
        requisition.Amount.ShouldBe(30m);
        requisition.Revision.ShouldBe(revision);
    }
}
