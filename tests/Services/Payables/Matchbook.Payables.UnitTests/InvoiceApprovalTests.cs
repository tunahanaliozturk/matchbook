using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

public sealed class InvoiceApprovalTests
{
    private static readonly PurchaseOrder Order = A.IssuedOrder(A.Ordered(1, 10, 10m));

    [Fact]
    public void An_approver_who_did_not_capture_the_invoice_can_accept_its_price_variance_and_it_matches()
    {
        Invoice invoice = OnPriceVariance();

        invoice.AcceptPriceVariance(A.Approver, "  Freight surcharge agreed with the buyer.  ", A.Now);
        MatchResult result = invoice.Evaluate(Order, A.Received((1, 10)), 30, A.Now);

        result.Outcome.ShouldBe(MatchOutcome.Matched);
        invoice.Status.ShouldBe(InvoiceStatus.Payable);
        invoice.Reason.ShouldBe(MatchReason.PriceVarianceAccepted);
        invoice.VarianceAcceptedBy.ShouldBe(A.Approver.Id);
        invoice.VarianceAcceptanceReason.ShouldBe("Freight surcharge agreed with the buyer.");
    }

    [Fact]
    public void An_accepted_variance_still_waits_for_goods_that_another_invoice_took_in_the_meantime()
    {
        Invoice invoice = OnPriceVariance();
        invoice.AcceptPriceVariance(A.Approver, "Agreed.", A.Now);

        invoice.Evaluate(Order, A.Position(received: [(1, 10)], invoiced: [(1, 6)]), 30, A.Now);
        invoice.Status.ShouldBe(InvoiceStatus.AwaitingReceipt);

        invoice.Evaluate(Order, A.Position(received: [(1, 15)], invoiced: [(1, 6)]), 30, A.Now);
        invoice.Status.ShouldBe(InvoiceStatus.Payable);
    }

    [Fact]
    public void The_person_who_captured_an_invoice_cannot_accept_its_variance_even_as_an_approver()
    {
        Actor both = A.Person(Roles.ApClerk, Roles.ApApprover);
        Invoice invoice = OnPriceVariance(capturedBy: both);

        var refusal = Should.Throw<BusinessRuleException>(() => invoice.AcceptPriceVariance(both, "Mine.", A.Now));

        refusal.Code.ShouldBe("invoice.self_approval");
        refusal.Kind.ShouldBe(ViolationKind.Forbidden);
    }

    [Fact]
    public void Only_an_ap_approver_can_accept_a_variance()
    {
        var refusal = Should.Throw<BusinessRuleException>(() => OnPriceVariance().AcceptPriceVariance(A.Person(Roles.ApClerk), "Ok.", A.Now));

        refusal.Code.ShouldBe("invoice.approver_required");
        refusal.Kind.ShouldBe(ViolationKind.Forbidden);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepting_a_variance_needs_a_reason(string reason) =>
        Should.Throw<BusinessRuleException>(() => OnPriceVariance().AcceptPriceVariance(A.Approver, reason, A.Now))
            .Code.ShouldBe("invoice.reason_required");

    [Fact]
    public void A_reason_is_at_most_five_hundred_characters() =>
        Should.Throw<BusinessRuleException>(() => OnPriceVariance().AcceptPriceVariance(A.Approver, new string('x', 501), A.Now))
            .Code.ShouldBe("invoice.reason_required");

    [Fact]
    public void Only_an_invoice_on_a_price_variance_can_have_one_accepted()
    {
        Invoice invoice = A.Invoice(A.Line(1, 10, 10.5m));
        invoice.Evaluate(Order, A.Received((1, 2)), 30, A.Now);

        var refusal = Should.Throw<BusinessRuleException>(() => invoice.AcceptPriceVariance(A.Approver, "Ok.", A.Now));

        refusal.Code.ShouldBe("invoice.no_price_variance");
        refusal.Kind.ShouldBe(ViolationKind.Conflict);
    }

    [Fact]
    public void A_suspected_duplicate_cleared_by_an_approver_goes_on_to_the_match()
    {
        Invoice invoice = A.Invoice(A.Line(1, 10, 10m));
        Guid original = Guid.NewGuid();
        invoice.HoldAsSuspectedDuplicate(original);
        invoice.Status.ShouldBe(InvoiceStatus.SuspectedDuplicate);
        invoice.SuspectedDuplicateOf.ShouldBe(original);

        invoice.ClearSuspectedDuplicate(A.Approver, A.Now);
        invoice.Status.ShouldBe(InvoiceStatus.Captured);
        invoice.DuplicateClearedBy.ShouldBe(A.Approver.Id);

        invoice.Evaluate(Order, A.Received((1, 10)), 30, A.Now);
        invoice.Status.ShouldBe(InvoiceStatus.Payable);
    }

    [Fact]
    public void The_person_who_captured_an_invoice_cannot_clear_it_as_a_duplicate()
    {
        Actor both = A.Person(Roles.ApClerk, Roles.ApApprover);
        Invoice invoice = A.Invoice("INV-1", A.Today, A.SupplierId, both, A.Line(1, 10, 10m));
        invoice.HoldAsSuspectedDuplicate(Guid.NewGuid());

        Should.Throw<BusinessRuleException>(() => invoice.ClearSuspectedDuplicate(both, A.Now)).Code.ShouldBe("invoice.self_approval");
    }

    [Fact]
    public void Only_an_ap_approver_can_clear_a_suspected_duplicate()
    {
        Invoice invoice = A.Invoice(A.Line(1, 10, 10m));
        invoice.HoldAsSuspectedDuplicate(Guid.NewGuid());

        Should.Throw<BusinessRuleException>(() => invoice.ClearSuspectedDuplicate(A.Treasurer, A.Now)).Code.ShouldBe("invoice.approver_required");
    }

    [Fact]
    public void Only_an_invoice_held_as_a_duplicate_can_be_cleared() =>
        Should.Throw<BusinessRuleException>(() => A.Invoice(A.Line(1, 10, 10m)).ClearSuspectedDuplicate(A.Approver, A.Now))
            .Code.ShouldBe("invoice.not_suspected_duplicate");

    [Fact]
    public void Only_a_freshly_captured_invoice_is_held_as_a_duplicate()
    {
        Invoice invoice = A.Invoice(A.Line(1, 10, 10m));
        invoice.Evaluate(Order, A.Received((1, 10)), 30, A.Now);

        Should.Throw<InvalidOperationException>(() => invoice.HoldAsSuspectedDuplicate(Guid.NewGuid()));
    }

    private static Invoice OnPriceVariance(Actor? capturedBy = null)
    {
        Invoice invoice = A.Invoice("INV-1", A.Today, A.SupplierId, capturedBy ?? A.Clerk, A.Line(1, 5, 10.5m));
        invoice.Evaluate(Order, A.Received((1, 10)), 30, A.Now);
        invoice.Status.ShouldBe(InvoiceStatus.PriceVariance);
        return invoice;
    }
}
