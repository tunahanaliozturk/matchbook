using Matchbook.Payables.Domain.Invoices;

namespace Matchbook.Payables.UnitTests;

public sealed class SuspectedDuplicateTests
{
    private static readonly Invoice NewInvoice = A.Invoice("INV-2002", A.Today, A.SupplierId, A.Clerk, A.Line(1, 4, 25m));

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-7)]
    public void The_same_supplier_and_total_within_seven_days_under_another_number_is_suspect(int daysApart)
    {
        DuplicateCandidate existing = Existing("INV-2001", daysApart);

        SuspectedDuplicate.FindFor(NewInvoice, [existing]).ShouldBe(existing);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(-8)]
    public void Invoices_more_than_seven_days_apart_are_not_suspect(int daysApart) =>
        SuspectedDuplicate.FindFor(NewInvoice, [Existing("INV-2001", daysApart)]).ShouldBeNull();

    [Fact]
    public void Another_total_is_not_suspect() =>
        SuspectedDuplicate.FindFor(NewInvoice, [Existing("INV-2001", 0) with { Total = 100.01m }]).ShouldBeNull();

    [Fact]
    public void Another_suppliers_invoice_is_not_suspect() =>
        SuspectedDuplicate.FindFor(NewInvoice, [Existing("INV-2001", 0) with { SupplierId = A.OtherSupplierId }]).ShouldBeNull();

    [Fact]
    public void The_same_number_is_an_outright_duplicate_and_not_this_rule() =>
        SuspectedDuplicate.FindFor(NewInvoice, [Existing("inv 2002", 0)]).ShouldBeNull();

    [Fact]
    public void A_rejected_invoice_is_no_claim_and_raises_no_suspicion() =>
        SuspectedDuplicate.FindFor(NewInvoice, [Existing("INV-2001", 0) with { Status = InvoiceStatus.Rejected }]).ShouldBeNull();

    [Fact]
    public void An_invoice_does_not_suspect_itself() =>
        SuspectedDuplicate.FindFor(NewInvoice, [Existing("INV-2001", 0) with { InvoiceId = NewInvoice.Id }]).ShouldBeNull();

    [Fact]
    public void Among_several_lookalikes_the_one_with_the_lowest_id_is_named()
    {
        DuplicateCandidate earlier = Existing("INV-1990", 1) with { InvoiceId = Guid.Parse("00000000-0000-7000-8000-000000000001") };
        DuplicateCandidate later = Existing("INV-1991", 1) with { InvoiceId = Guid.Parse("00000000-0000-7000-8000-000000000002") };

        SuspectedDuplicate.FindFor(NewInvoice, [later, earlier]).ShouldBe(earlier);
    }

    private static DuplicateCandidate Existing(string number, int daysApart) =>
        new(
            Guid.NewGuid(),
            A.SupplierId,
            InvoiceNumber.Normalise(number),
            A.Today.AddDays(daysApart),
            NewInvoice.Total,
            InvoiceStatus.Payable);
}
