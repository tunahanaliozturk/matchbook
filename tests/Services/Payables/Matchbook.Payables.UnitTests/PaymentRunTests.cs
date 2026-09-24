using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

public sealed class PaymentRunTests
{
    private static readonly Guid Blocked = Guid.Parse("5a000000-0000-4000-8000-000000000003");
    private static readonly Guid Unverified = Guid.Parse("5a000000-0000-4000-8000-000000000004");
    private static readonly Guid Unknown = Guid.Parse("5a000000-0000-4000-8000-000000000005");

    private static readonly Dictionary<Guid, Supplier> Suppliers = new()
    {
        [A.SupplierId] = A.Supplier(A.SupplierId, paymentTermsDays: 30),
        [A.OtherSupplierId] = A.Supplier(A.OtherSupplierId, accountVersion: 4, paymentTermsDays: 10),
        [Blocked] = A.Supplier(Blocked, active: false),
        [Unverified] = A.Supplier(Unverified, accountVersion: null),
    };

    [Fact]
    public void A_draft_takes_every_payable_invoice_due_by_the_execution_date_for_a_supplier_that_can_be_paid()
    {
        PaymentCandidate due = Candidate(A.SupplierId, 100m, dueInDays: 0);
        PaymentCandidate dueEarlier = Candidate(A.OtherSupplierId, 50.5m, dueInDays: -20);

        PaymentRunDraft draft = Draft(
            due,
            dueEarlier,
            Candidate(A.SupplierId, 1m, dueInDays: 1),
            Candidate(A.SupplierId, 1m, dueInDays: -1) with { Status = InvoiceStatus.Scheduled },
            Candidate(Blocked, 1m, dueInDays: -1),
            Candidate(Unverified, 1m, dueInDays: -1),
            Candidate(Unknown, 1m, dueInDays: -1));

        draft.Items.Select(item => item.InvoiceId).ShouldBe([due.InvoiceId, dueEarlier.InvoiceId], ignoreOrder: true);
        draft.Items.ShouldAllBe(item => item.Status == PaymentRunItemStatus.Scheduled && item.PaymentRunId == draft.Run.Id);
        draft.Run.Status.ShouldBe(PaymentRunStatus.Draft);
        draft.Run.ItemCount.ShouldBe(2);
        draft.Run.Total.ShouldBe(150.5m);
        draft.Run.DraftedBy.ShouldBe(A.Treasurer.Id);
    }

    [Fact]
    public void A_draft_records_per_supplier_the_verified_account_it_would_pay()
    {
        PaymentRunDraft draft = Draft(
            Candidate(A.SupplierId, 10m, dueInDays: 0),
            Candidate(A.SupplierId, 20m, dueInDays: 0),
            Candidate(A.OtherSupplierId, 5m, dueInDays: 0));

        draft.Run.Creditors.Select(creditor => (creditor.SupplierId, creditor.AccountVersion, creditor.ItemCount, creditor.Total))
            .ShouldBe([(A.SupplierId, 1, 2, 30m), (A.OtherSupplierId, 4, 1, 5m)], ignoreOrder: true);
        draft.Run.Creditors.ShouldAllBe(creditor => creditor.Status == CreditorStatus.Scheduled);
        PaymentRunCreditor recorded = draft.Run.Creditors.First(creditor => creditor.SupplierId == A.SupplierId);
        recorded.ProtectedIban.ShouldBe("v1.test.account-1");
        recorded.IbanLastFour.ShouldBe("3000");
    }

    [Fact]
    public void An_invoice_matched_before_its_supplier_arrived_is_due_on_the_terms_the_supplier_has_now()
    {
        PaymentCandidate matchedEarly = Candidate(A.SupplierId, 10m, dueInDays: null) with { InvoiceDate = A.Today.AddDays(-30) };
        PaymentCandidate notYet = Candidate(A.SupplierId, 10m, dueInDays: null) with { InvoiceDate = A.Today.AddDays(-29) };

        Draft(matchedEarly, notYet).Items.Select(item => item.InvoiceId).ShouldBe([matchedEarly.InvoiceId]);
    }

    [Fact]
    public void An_invoice_listed_twice_is_taken_once()
    {
        PaymentCandidate candidate = Candidate(A.SupplierId, 10m, dueInDays: 0);

        PaymentRunDraft draft = Draft(candidate, candidate);

        draft.Items.Count.ShouldBe(1);
        draft.Run.Total.ShouldBe(10m);
    }

    [Fact]
    public void Nothing_due_means_no_run() =>
        Refusal(() => Draft(Candidate(A.SupplierId, 10m, dueInDays: 5))).ShouldBe(("payment_run.nothing_due", ViolationKind.Conflict));

    [Fact]
    public void A_run_cannot_be_drafted_for_a_date_that_has_passed() =>
        Refusal(() => PaymentRun.Draft(Guid.NewGuid(), A.Today.AddDays(-1), A.Treasurer, [Candidate(A.SupplierId, 1m, -5)], Suppliers, A.Now))
            .ShouldBe(("payment_run.execution_date_past", ViolationKind.Invalid));

    [Fact]
    public void Only_a_treasurer_drafts_a_run() =>
        Refusal(() => PaymentRun.Draft(Guid.NewGuid(), A.Today, A.Approver, [Candidate(A.SupplierId, 1m, 0)], Suppliers, A.Now))
            .ShouldBe(("payment_run.treasurer_required", ViolationKind.Forbidden));

    [Fact]
    public void A_second_treasurer_releases_the_run_and_everything_is_paid()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0), Candidate(A.OtherSupplierId, 2.5m, 0)).Run;

        run.Release(A.SecondTreasurer, Suppliers, A.Now.AddHours(1));

        run.Status.ShouldBe(PaymentRunStatus.Released);
        run.ReleasedBy.ShouldBe(A.SecondTreasurer.Id);
        run.ReleasedAt.ShouldBe(A.Now.AddHours(1));
        run.Creditors.ShouldAllBe(creditor => creditor.Status == CreditorStatus.Paid);
        run.PaidCount.ShouldBe(2);
        run.PaidTotal.ShouldBe(12.5m);
    }

    [Fact]
    public void The_treasurer_who_drafted_a_run_cannot_release_it()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0)).Run;

        Refusal(() => run.Release(A.Treasurer, Suppliers, A.Now)).ShouldBe(("payment_run.same_treasurer", ViolationKind.Forbidden));
        run.Status.ShouldBe(PaymentRunStatus.Draft);
    }

    [Fact]
    public void Only_a_treasurer_releases_a_run() =>
        Refusal(() => Draft(Candidate(A.SupplierId, 10m, 0)).Run.Release(A.Approver, Suppliers, A.Now))
            .ShouldBe(("payment_run.treasurer_required", ViolationKind.Forbidden));

    [Fact]
    public void Releasing_a_run_twice_is_refused_the_second_time()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0)).Run;
        run.Release(A.SecondTreasurer, Suppliers, A.Now);

        Refusal(() => run.Release(A.SecondTreasurer, Suppliers, A.Now)).ShouldBe(("payment_run.not_draft", ViolationKind.Conflict));
        run.PaidCount.ShouldBe(1);
    }

    [Fact]
    public void At_release_a_supplier_blocked_since_the_draft_is_dropped_and_the_rest_is_paid()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0), Candidate(A.OtherSupplierId, 7m, 0)).Run;
        Dictionary<Guid, Supplier> now = new(Suppliers) { [A.OtherSupplierId] = A.Supplier(A.OtherSupplierId, active: false, accountVersion: 4) };

        run.Release(A.SecondTreasurer, now, A.Now);

        Creditor(run, A.OtherSupplierId).Status.ShouldBe(CreditorStatus.Dropped);
        Creditor(run, A.OtherSupplierId).DropReason.ShouldBe(CreditorDropReason.SupplierNotActive);
        Creditor(run, A.SupplierId).Status.ShouldBe(CreditorStatus.Paid);
        run.PaidCount.ShouldBe(1);
        run.PaidTotal.ShouldBe(10m);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(null)]
    public void At_release_a_supplier_whose_account_changed_since_the_draft_is_dropped(int? accountVersionNow)
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0), Candidate(A.OtherSupplierId, 7m, 0)).Run;
        Dictionary<Guid, Supplier> now = new(Suppliers) { [A.OtherSupplierId] = A.Supplier(A.OtherSupplierId, accountVersion: accountVersionNow) };

        run.Release(A.SecondTreasurer, now, A.Now);

        Creditor(run, A.OtherSupplierId).DropReason.ShouldBe(CreditorDropReason.AccountChanged);
        run.PaidTotal.ShouldBe(10m);
    }

    [Fact]
    public void A_supplier_the_release_cannot_find_is_not_paid()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0), Candidate(A.OtherSupplierId, 7m, 0)).Run;
        Dictionary<Guid, Supplier> now = new(Suppliers);
        now.Remove(A.OtherSupplierId);

        run.Release(A.SecondTreasurer, now, A.Now);

        Creditor(run, A.OtherSupplierId).DropReason.ShouldBe(CreditorDropReason.SupplierNotActive);
    }

    [Fact]
    public void A_run_in_which_nobody_can_be_paid_any_more_is_not_released_and_is_left_as_it_was()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0)).Run;
        Dictionary<Guid, Supplier> now = new() { [A.SupplierId] = A.Supplier(A.SupplierId, active: false) };

        Refusal(() => run.Release(A.SecondTreasurer, now, A.Now)).ShouldBe(("payment_run.nothing_payable", ViolationKind.Conflict));

        run.Status.ShouldBe(PaymentRunStatus.Draft);
        run.Creditors.ShouldAllBe(creditor => creditor.Status == CreditorStatus.Scheduled && creditor.DropReason == null);
    }

    [Fact]
    public void A_run_whose_execution_date_has_passed_is_not_released() =>
        Refusal(() => Draft(Candidate(A.SupplierId, 10m, 0)).Run.Release(A.SecondTreasurer, Suppliers, A.Now.AddDays(1)))
            .ShouldBe(("payment_run.execution_date_passed", ViolationKind.Conflict));

    [Fact]
    public void A_draft_can_be_cancelled_by_any_treasurer_including_the_one_who_drafted_it()
    {
        PaymentRun run = Draft(Candidate(A.SupplierId, 10m, 0)).Run;

        run.Cancel(A.Treasurer, A.Now);

        run.Status.ShouldBe(PaymentRunStatus.Cancelled);
        run.CancelledBy.ShouldBe(A.Treasurer.Id);
        run.CancelledAt.ShouldBe(A.Now);
    }

    [Fact]
    public void A_released_run_cannot_be_cancelled_and_a_cancelled_one_cannot_be_released()
    {
        PaymentRun released = Draft(Candidate(A.SupplierId, 10m, 0)).Run;
        released.Release(A.SecondTreasurer, Suppliers, A.Now);
        PaymentRun cancelled = Draft(Candidate(A.SupplierId, 10m, 0)).Run;
        cancelled.Cancel(A.Treasurer, A.Now);

        Refusal(() => released.Cancel(A.Treasurer, A.Now)).ShouldBe(("payment_run.not_draft", ViolationKind.Conflict));
        Refusal(() => cancelled.Release(A.SecondTreasurer, Suppliers, A.Now)).ShouldBe(("payment_run.not_draft", ViolationKind.Conflict));
    }

    [Fact]
    public void Only_a_treasurer_cancels_a_run() =>
        Refusal(() => Draft(Candidate(A.SupplierId, 10m, 0)).Run.Cancel(A.Clerk, A.Now))
            .ShouldBe(("payment_run.treasurer_required", ViolationKind.Forbidden));

    internal static PaymentCandidate Candidate(Guid supplierId, decimal amount, int? dueInDays) =>
        new(
            Guid.NewGuid(),
            A.OrderId,
            supplierId,
            "INV-" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            amount,
            InvoiceStatus.Payable,
            A.Today.AddDays(-40),
            dueInDays is { } days ? A.Today.AddDays(days) : null);

    private static PaymentRunDraft Draft(params PaymentCandidate[] candidates) =>
        PaymentRun.Draft(Guid.CreateVersion7(A.Now), A.Today, A.Treasurer, candidates, Suppliers, A.Now);

    private static PaymentRunCreditor Creditor(PaymentRun run, Guid supplierId) =>
        run.Creditors.Single(creditor => creditor.SupplierId == supplierId);

    private static (string Code, ViolationKind Kind) Refusal(Action action)
    {
        var refusal = Should.Throw<BusinessRuleException>(action);
        return (refusal.Code, refusal.Kind);
    }
}
