using Matchbook.Contracts.Payables;
using Matchbook.Payables.Application.Invoices;
using Matchbook.Payables.Application.PaymentRuns;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>What happens to a supplier that changes between a run's draft and its release.</summary>
public sealed class SupplierChangesAtReleaseTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>
{
    private readonly Scenario _given = new(fixture);

    [Fact]
    public async Task A_supplier_blocked_or_given_a_new_account_after_the_draft_is_dropped_and_its_invoices_stay_payable()
    {
        Guid blocked = await _given.SupplierAsync("NL91ABNA0417164300");
        Guid rebanked = await _given.SupplierAsync("DE89370400440532013000");
        Guid unchanged = await _given.SupplierAsync("GB82WEST12345698765432");
        InvoiceView ofBlocked = await _given.PayableInvoiceAsync(blocked, 100m);
        InvoiceView ofRebanked = await _given.PayableInvoiceAsync(rebanked, 200m);
        InvoiceView ofUnchanged = await _given.PayableInvoiceAsync(unchanged, 300m);
        PaymentRunView draft = await _given.DraftAsync(TestUsers.Tess);

        await _given.SupplierAsync("NL91ABNA0417164300", id: blocked, version: 2, active: false);
        await _given.SupplierAsync("GB29NWBK60161331926819", id: rebanked, version: 2, accountVersion: 2);
        PaymentRunView released = await _given.ReleaseAsync(TestUsers.Trevor, draft.Id);

        Creditor(released, blocked).Status.ShouldBe(CreditorStatus.Dropped);
        Creditor(released, blocked).DropReason.ShouldBe(CreditorDropReason.SupplierNotActive);
        Creditor(released, rebanked).Status.ShouldBe(CreditorStatus.Dropped);
        Creditor(released, rebanked).DropReason.ShouldBe(CreditorDropReason.AccountChanged);
        Creditor(released, unchanged).Status.ShouldBe(CreditorStatus.Paid);
        released.PaidCount.ShouldBe(1);
        released.PaidTotal.ShouldBe(300m);

        (await _given.InvoiceAsync(ofBlocked.Id)).Status.ShouldBe(InvoiceStatus.Payable);
        (await _given.InvoiceAsync(ofRebanked.Id)).Status.ShouldBe(InvoiceStatus.Payable);
        (await _given.InvoiceAsync(ofUnchanged.Id)).Status.ShouldBe(InvoiceStatus.Paid);
        (await _given.DbAsync(db => db.PaymentRunItems
                .Where(item => item.PaymentRunId == draft.Id)
                .ToDictionaryAsync(item => item.InvoiceId, item => item.Status)))
            .ShouldBe(new Dictionary<Guid, PaymentRunItemStatus>
            {
                [ofBlocked.Id] = PaymentRunItemStatus.Dropped,
                [ofRebanked.Id] = PaymentRunItemStatus.Dropped,
                [ofUnchanged.Id] = PaymentRunItemStatus.Paid,
            });

        await _given.Probe.WaitForAsync<InvoicePaid>(paid => paid.InvoiceId == ofUnchanged.Id);
        await _given.Probe.ShouldNotReceiveAsync<InvoicePaid>(
            paid => paid.InvoiceId == ofBlocked.Id || paid.InvoiceId == ofRebanked.Id,
            TimeSpan.FromSeconds(2));

        // The rebanked supplier's invoice is free for the next run, on the new account.
        PaymentRunView next = await _given.DraftAsync(TestUsers.Tess);
        Creditor(next, rebanked).AccountVersion.ShouldBe(2);
        Creditor(next, rebanked).MaskedIban.ShouldBe("****6819");
        next.Creditors.ShouldNotContain(creditor => creditor.SupplierId == blocked);
    }

    private static PaymentRunCreditorView Creditor(PaymentRunView run, Guid supplierId) =>
        run.Creditors.Single(creditor => creditor.SupplierId == supplierId);
}
