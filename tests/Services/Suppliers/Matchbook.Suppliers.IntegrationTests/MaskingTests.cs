using Matchbook.SharedKernel;
using Matchbook.Suppliers.Api;
using Matchbook.Testing;

namespace Matchbook.Suppliers.IntegrationTests;

[Collection(SharedHost.Name)]
public sealed class MaskingTests(SuppliersFixture fixture)
{
    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task Only_the_approver_reviewing_a_pending_proposal_sees_its_iban_in_full()
    {
        string inForce = SupplierApi.NewIban();
        string proposed = SupplierApi.NewIban();
        Guid id = (await _api.ActiveSupplierAsync(inForce)).Id;
        SupplierResponse proposersView = await _api.ProposeAsync(TestUsers.Sam, id, proposed);

        SupplierResponse reviewersView = await _api.GetAsync(TestUsers.Sofia, id);

        BankAccountResponse pending = reviewersView.BankAccounts.Single(account => account.Status == BankAccountStatus.Pending);
        (pending.Iban, pending.IbanMasked).ShouldBe((proposed, false));
        BankAccountResponse current = reviewersView.BankAccounts.Single(account => account.Status == BankAccountStatus.Approved);
        (current.Iban, current.IbanMasked).ShouldBe(("****" + inForce[^4..], true));

        proposersView.BankAccounts.ShouldAllBe(account => account.IbanMasked);
        foreach (Actor viewer in (Actor[])[TestUsers.Sam, TestUsers.Audrey])
        {
            string body = await (await _api.As(viewer).GetAsync(new Uri($"/suppliers/{id}", UriKind.Relative)))
                .Content.ReadAsStringAsync();

            body.ShouldNotContain(proposed[4..]);
            body.ShouldNotContain(inForce[4..]);
        }

        // Once decided, the reviewer is no longer reviewing it.
        SupplierResponse approved = await _api.DoAsync(TestUsers.Sofia, id, $"bank-accounts/{pending.Id}/approve");
        approved.BankAccounts.ShouldAllBe(account => account.IbanMasked);
    }

    [Fact]
    public async Task The_supplier_list_carries_no_iban_at_all()
    {
        Guid id = (await _api.ActiveSupplierAsync(SupplierApi.NewIban())).Id;
        await _api.ProposeAsync(TestUsers.Sam, id, SupplierApi.NewIban());

        string page = await (await _api.As(TestUsers.Sofia).GetAsync(
            new Uri("/suppliers?hasPendingBankAccount=true&limit=200", UriKind.Relative))).Content.ReadAsStringAsync();

        page.ShouldContain(id.ToString());
        page.ShouldNotContain("iban", Case.Insensitive);
    }
}
