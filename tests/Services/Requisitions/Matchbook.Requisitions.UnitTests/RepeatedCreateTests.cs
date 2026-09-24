using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.UnitTests;

public sealed class RepeatedCreateTests
{
    private static readonly Guid ClientId = Guid.Parse("d0000000-0000-4000-8000-000000000001");

    [Fact]
    public void A_draft_keeps_the_id_the_client_chose() =>
        Requisition.Draft(ClientId, 1, A.Requester, A.Details(), A.Now).Id.ShouldBe(ClientId);

    [Fact]
    public void The_same_request_sent_again_is_a_repeat_even_spelled_differently()
    {
        RequisitionDetails sent = A.Details(A.Line(2m, 1_500m), A.Line(1m, 12.5m, "Dock"));
        Requisition requisition = Requisition.Draft(ClientId, 1, A.Requester, sent, A.Now);

        RequisitionDetails again = sent with
        {
            CostCentreCode = " eng-platform ",
            Justification = sent.Justification + "  ",
            Lines = [A.Line(2.000m, 1_500.00m), new LineInput(" Dock ", 1m, "EA ", 12.50m)],
        };

        requisition.IsRepeatOf(A.Requester, again).ShouldBeTrue();
    }

    [Fact]
    public void A_request_with_other_lines_is_not_a_repeat()
    {
        Requisition requisition = Requisition.Draft(ClientId, 1, A.Requester, A.Details(A.Line(2m, 1_500m)), A.Now);

        requisition.IsRepeatOf(A.Requester, A.Details(A.Line(3m, 1_500m))).ShouldBeFalse();
        requisition.IsRepeatOf(A.Requester, A.Details(A.Line(2m, 1_500m), A.Line())).ShouldBeFalse();
    }

    [Fact]
    public void The_same_request_from_someone_else_is_not_a_repeat()
    {
        Requisition requisition = Requisition.Draft(ClientId, 1, A.Requester, A.Details(), A.Now);

        requisition.IsRepeatOf(A.Manager, A.Details()).ShouldBeFalse();
    }

    [Fact]
    public void After_an_edit_the_original_request_no_longer_describes_the_requisition()
    {
        Requisition requisition = Requisition.Draft(ClientId, 1, A.Requester, A.Details(A.Line(2m, 1_500m)), A.Now);
        requisition.Edit(A.Requester, A.Details(A.Line(1m, 1_500m)), A.Now);

        requisition.IsRepeatOf(A.Requester, A.Details(A.Line(2m, 1_500m))).ShouldBeFalse();
    }
}
