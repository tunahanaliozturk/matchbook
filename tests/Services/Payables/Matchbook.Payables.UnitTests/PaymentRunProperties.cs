using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

public sealed class PaymentRunProperties
{
    private static readonly Guid[] SupplierIds = [.. Enumerable.Range(1, 5).Select(n => Guid.Parse($"5b000000-0000-4000-8000-00000000000{n}"))];

    [Property(MaxTest = 300)]
    public Property A_draft_takes_each_eligible_invoice_exactly_once_and_nothing_else() =>
        Prop.ForAll(Scenarios.ToArbitrary(), scenario =>
        {
            PaymentCandidate[] eligible = [.. scenario.Candidates.Where(candidate => IsEligible(candidate, scenario)).DistinctBy(candidate => candidate.InvoiceId)];

            if (eligible.Length == 0)
            {
                Should.Throw<BusinessRuleException>(() => Draft(scenario)).Code.ShouldBe("payment_run.nothing_due");
                return;
            }

            PaymentRunDraft draft = Draft(scenario);

            draft.Items.Select(item => item.InvoiceId).ShouldBeUnique();
            draft.Items.Select(item => item.InvoiceId).ShouldBe(eligible.Select(candidate => candidate.InvoiceId), ignoreOrder: true);
            foreach (PaymentRunItem item in draft.Items)
            {
                PaymentCandidate source = scenario.Candidates.First(candidate => candidate.InvoiceId == item.InvoiceId);
                DueDate(source, scenario).ShouldBeLessThanOrEqualTo(scenario.ExecutionDate);
                item.Amount.ShouldBe(source.Amount);
            }
        });

    [Property(MaxTest = 300)]
    public Property A_drafts_totals_agree_with_its_items_per_supplier_and_overall() =>
        Prop.ForAll(Scenarios.ToArbitrary(), scenario =>
        {
            if (!scenario.Candidates.Any(candidate => IsEligible(candidate, scenario)))
            {
                return;
            }

            PaymentRunDraft draft = Draft(scenario);

            draft.Run.Total.ShouldBe(draft.Items.Sum(item => item.Amount));
            draft.Run.ItemCount.ShouldBe(draft.Items.Count);
            foreach (PaymentRunCreditor creditor in draft.Run.Creditors)
            {
                PaymentRunItem[] own = [.. draft.Items.Where(item => item.SupplierId == creditor.SupplierId)];
                creditor.ItemCount.ShouldBe(own.Length);
                creditor.Total.ShouldBe(own.Sum(item => item.Amount));
                creditor.AccountVersion.ShouldBe(scenario.Suppliers[creditor.SupplierId].Account!.AccountVersion);
            }
        });

    [Property(MaxTest = 300)]
    public Property A_release_pays_exactly_the_suppliers_still_active_on_the_same_account() =>
        Prop.ForAll(Scenarios.ToArbitrary(), Changes.ToArbitrary(), (scenario, changes) =>
        {
            if (!scenario.Candidates.Any(candidate => IsEligible(candidate, scenario)))
            {
                return;
            }

            PaymentRun run = Draft(scenario).Run;
            Dictionary<Guid, Supplier> atRelease = scenario.Suppliers.ToDictionary(
                pair => pair.Key,
                pair => changes.TryGetValue(pair.Key, out Supplier? changed) ? changed : pair.Value);
            PaymentRunCreditor[] payable = [.. run.Creditors.Where(creditor =>
                atRelease.TryGetValue(creditor.SupplierId, out Supplier? supplier)
                && supplier.IsActive
                && supplier.Account?.AccountVersion == creditor.AccountVersion)];

            if (payable.Length == 0)
            {
                Should.Throw<BusinessRuleException>(() => run.Release(A.SecondTreasurer, atRelease, A.Now)).Code.ShouldBe("payment_run.nothing_payable");
                run.Status.ShouldBe(PaymentRunStatus.Draft);
                return;
            }

            run.Release(A.SecondTreasurer, atRelease, A.Now);

            run.Creditors.Where(creditor => creditor.Status == CreditorStatus.Paid).ShouldBe(payable, ignoreOrder: true);
            run.PaidCount.ShouldBe(payable.Sum(creditor => creditor.ItemCount));
            run.PaidTotal.ShouldBe(payable.Sum(creditor => creditor.Total));
        });

    private static PaymentRunDraft Draft(Scenario scenario) =>
        PaymentRun.Draft(Guid.NewGuid(), scenario.ExecutionDate, A.Treasurer, scenario.Candidates, scenario.Suppliers, A.Now);

    // The rule, restated independently of the code: payable, due by the date, and owed to a supplier that is active
    // with a verified account.
    private static bool IsEligible(PaymentCandidate candidate, Scenario scenario) =>
        candidate.Status == InvoiceStatus.Payable
        && scenario.Suppliers.TryGetValue(candidate.SupplierId, out Supplier? supplier)
        && supplier.IsActive
        && supplier.Account is not null
        && DueDate(candidate, scenario) <= scenario.ExecutionDate;

    private static DateOnly DueDate(PaymentCandidate candidate, Scenario scenario) =>
        candidate.DueDate ?? candidate.InvoiceDate.AddDays(scenario.Suppliers[candidate.SupplierId].PaymentTermsDays);

    private sealed record Scenario(DateOnly ExecutionDate, Dictionary<Guid, Supplier> Suppliers, PaymentCandidate[] Candidates);

    // Four known suppliers in every state a run cares about; the fifth is one Payables has not heard of.
    private sealed record SupplierState(bool Active, int? AccountVersion, int PaymentTermsDays);

    private static readonly Gen<SupplierState> SupplierStates =
        from active in Gen.Frequency((3, Gen.Constant(true)), (1, Gen.Constant(false)))
        from accountVersion in Gen.Frequency((1, Gen.Constant<int?>(null)), (4, Gen.Choose(1, 3).Select(version => (int?)version)))
        from terms in Gen.Choose(0, 60)
        select new SupplierState(active, accountVersion, terms);

    private static readonly Gen<PaymentCandidate> Candidates =
        from supplier in Gen.Elements(SupplierIds)
        from cents in Gen.Choose(1, 5_000_000)
        from status in Gen.Frequency(
            (6, Gen.Constant(InvoiceStatus.Payable)),
            (1, Gen.Elements(InvoiceStatus.Scheduled, InvoiceStatus.Paid, InvoiceStatus.PriceVariance)))
        from invoiceAge in Gen.Choose(0, 120)
        from dueOffset in Gen.Frequency((1, Gen.Constant<int?>(null)), (4, Gen.Choose(-60, 60).Select(days => (int?)days)))
        select new PaymentCandidate(
            Guid.NewGuid(),
            A.OrderId,
            supplier,
            "INV-" + cents,
            cents / 100m,
            status,
            A.Today.AddDays(-invoiceAge),
            dueOffset is { } offset ? A.Today.AddDays(offset) : null);

    private static readonly Gen<Scenario> Scenarios =
        from states in Gen.ArrayOf(SupplierStates, 4)
        from candidates in Candidates.ListOf()
        from repeats in Gen.SubListOf<PaymentCandidate>(candidates)
        from daysAhead in Gen.Choose(0, 30)
        select new Scenario(
            A.Today.AddDays(daysAhead),
            SupplierIds.Take(4).Select((id, i) => Rebuild(id, states[i])).ToDictionary(supplier => supplier.Id),
            [.. candidates, .. repeats]);

    /// <summary>For some suppliers, a new state at release: blocked, unblocked, or a new or removed account.</summary>
    private static readonly Gen<Dictionary<Guid, Supplier>> Changes =
        from changed in Gen.SubListOf(SupplierIds.Take(4))
        from states in Gen.ArrayOf(SupplierStates, 4)
        select changed.Select((id, i) => Rebuild(id, states[i])).ToDictionary(supplier => supplier.Id);

    private static Supplier Rebuild(Guid id, SupplierState state) =>
        A.Supplier(id, state.Active, state.AccountVersion, state.PaymentTermsDays);
}
