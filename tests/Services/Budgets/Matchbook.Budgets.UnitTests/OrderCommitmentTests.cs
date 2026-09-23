using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.UnitTests;

public sealed class OrderCommitmentTests
{
    private static readonly Guid Order = Guid.CreateVersion7();
    private static readonly Guid Requisition = Guid.CreateVersion7();

    private static OrderCommitment Committed(decimal amount)
    {
        OrderCommitment order = OrderCommitment.Track(Order, Requisition);
        order.Commit(Some.BudgetId, attempt: 1, amount);
        return order;
    }

    [Fact]
    public void A_first_request_for_an_order_has_to_be_decided() =>
        OrderCommitment.Track(Order, Requisition).Replay(1).ShouldBeNull();

    [Fact]
    public void Attempts_count_from_one() =>
        new Func<CommitmentReplay?>(() => OrderCommitment.Track(Order, Requisition).Replay(0))
            .ShouldBreak("funds.attempt_invalid", ViolationKind.Invalid);

    [Fact]
    public void After_a_refusal_only_a_newer_attempt_is_decided_again()
    {
        OrderCommitment order = OrderCommitment.Track(Order, Requisition);
        order.Refuse(attempt: 2, FundsRefusal.InsufficientFunds);

        order.Replay(1).ShouldBe(CommitmentReplay.Stale);
        order.Replay(2).ShouldBe(CommitmentReplay.Stale);
        order.Replay(3).ShouldBeNull();
        order.Refusal.ShouldBe(FundsRefusal.InsufficientFunds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void Any_request_for_a_committed_order_is_answered_as_committed(int attempt)
    {
        OrderCommitment order = OrderCommitment.Track(Order, Requisition);
        order.Commit(Some.BudgetId, attempt: 2, 900m);

        order.Replay(attempt).ShouldBe(CommitmentReplay.AlreadyCommitted);
    }

    [Fact]
    public void A_request_for_an_order_closed_before_it_was_committed_is_answered_as_closed()
    {
        OrderCommitment order = OrderCommitment.Track(Order, Requisition);
        order.Close(Some.Now, Some.Now).ShouldBeNull();

        order.Replay(1).ShouldBe(CommitmentReplay.Closed);
        order.WasCommitted.ShouldBeFalse();
    }

    [Fact]
    public void Deciding_an_attempt_twice_is_a_bug()
    {
        OrderCommitment order = Committed(900m);

        Should.Throw<InvalidOperationException>(() => order.Commit(Some.BudgetId, attempt: 2, 900m));
    }

    [Fact]
    public void Committing_holds_the_whole_order_amount()
    {
        OrderCommitment order = Committed(900m);

        order.Status.ShouldBe(CommitmentStatus.Committed);
        order.Amount.ShouldBe(900m);
        order.Remaining.ShouldBe(900m);
        order.Refusal.ShouldBeNull();
    }

    [Fact]
    public void An_invoice_within_the_commitment_moves_its_own_amount_to_actual()
    {
        OrderCommitment order = Committed(900m);
        Guid invoice = Guid.CreateVersion7();

        LedgerEntry entry = order.Invoice(invoice, 400m, Some.Now, Some.Now);

        entry.DocumentId.ShouldBe(invoice);
        entry.Step.ShouldBe(LedgerStep.Invoice);
        entry.Movement.ShouldBe(Movement.Invoice(relief: 400m, amount: 400m));
        order.Remaining.ShouldBe(500m);
    }

    [Fact]
    public void An_invoice_beyond_the_commitment_relieves_what_is_left_and_the_excess_is_still_actual()
    {
        OrderCommitment order = Committed(900m);
        order.Invoice(Guid.CreateVersion7(), 600m, Some.Now, Some.Now);

        LedgerEntry entry = order.Invoice(Guid.CreateVersion7(), 330m, Some.Now, Some.Now);

        entry.Movement.ShouldBe(Movement.Invoice(relief: 300m, amount: 330m));
        order.Remaining.ShouldBe(0m);
    }

    [Fact]
    public void Closing_releases_what_invoices_have_not_taken()
    {
        OrderCommitment order = Committed(900m);
        order.Invoice(Guid.CreateVersion7(), 600m, Some.Now, Some.Now);

        LedgerEntry? entry = order.Close(Some.Now, Some.Now);

        entry.ShouldNotBeNull();
        entry.DocumentId.ShouldBe(Order);
        entry.Step.ShouldBe(LedgerStep.Close);
        entry.Movement.ShouldBe(Movement.ReleaseCommitment(300m));
        order.Remaining.ShouldBe(0m);
        order.Close(Some.Now, Some.Now).ShouldBeNull();
    }

    [Fact]
    public void Closing_a_fully_invoiced_order_has_nothing_to_release()
    {
        OrderCommitment order = Committed(900m);
        order.Invoice(Guid.CreateVersion7(), 900m, Some.Now, Some.Now);

        order.Close(Some.Now, Some.Now).ShouldBeNull();
        order.Status.ShouldBe(CommitmentStatus.Closed);
    }

    [Fact]
    public void An_invoice_after_closing_goes_to_actual_in_full()
    {
        OrderCommitment order = Committed(900m);
        order.Close(Some.Now, Some.Now);

        order.Invoice(Guid.CreateVersion7(), 600m, Some.Now, Some.Now)
            .Movement.ShouldBe(Movement.Invoice(relief: 0m, amount: 600m));
    }

    [Fact]
    public void Invoice_then_close_and_close_then_invoice_leave_the_same_figures()
    {
        Budget invoicedFirst = Some.BudgetWith(allotted: 1_000m, committed: 900m);
        OrderCommitment one = Committed(900m);
        invoicedFirst.Apply(one.Invoice(Guid.CreateVersion7(), 950m, Some.Now, Some.Now).Movement);
        invoicedFirst.Apply(one.Close(Some.Now, Some.Now)?.Movement ?? default);

        Budget closedFirst = Some.BudgetWith(allotted: 1_000m, committed: 900m);
        OrderCommitment two = Committed(900m);
        closedFirst.Apply(two.Close(Some.Now, Some.Now)?.Movement ?? default);
        closedFirst.Apply(two.Invoice(Guid.CreateVersion7(), 950m, Some.Now, Some.Now).Movement);

        (invoicedFirst.Committed, invoicedFirst.Actual).ShouldBe((0m, 950m));
        (closedFirst.Committed, closedFirst.Actual).ShouldBe((0m, 950m));
    }

    [Fact]
    public void An_invoice_against_an_order_never_committed_is_refused() =>
        new Func<LedgerEntry>(() => OrderCommitment.Track(Order, Requisition).Invoice(Guid.CreateVersion7(), 10m, Some.Now, Some.Now))
            .ShouldBreak("funds.order_not_committed", ViolationKind.NotFound);

    [Fact]
    public void An_invoice_needs_a_positive_amount() =>
        new Func<LedgerEntry>(() => Committed(900m).Invoice(Guid.CreateVersion7(), 0m, Some.Now, Some.Now))
            .ShouldBreak("budget.amount_invalid", ViolationKind.Invalid);
}
