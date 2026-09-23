using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class VisibilityTests
{
    [Theory]
    [InlineData(Roles.Approver)]
    [InlineData(Roles.FinanceApprover)]
    [InlineData(Roles.Cfo)]
    [InlineData(Roles.Auditor)]
    public void Approvers_and_the_auditor_see_every_requisition(string role) =>
        A.Draft().IsVisibleTo(A.Person(99, "someone", role)).ShouldBeTrue();

    [Fact]
    public void A_requester_sees_their_own() => A.Draft().IsVisibleTo(A.Requester).ShouldBeTrue();

    [Theory]
    [InlineData(Roles.Requester)]
    [InlineData(Roles.Buyer)]
    [InlineData(Roles.BudgetAdmin)]
    public void Anyone_else_does_not_see_another_persons_requisition(string role) =>
        A.Draft().IsVisibleTo(A.Person(99, "someone", role)).ShouldBeFalse();
}
