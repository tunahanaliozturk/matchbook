using System.Text.Json;
using Matchbook.Contracts.Suppliers;
using Matchbook.Suppliers.Api;
using Matchbook.Testing;

namespace Matchbook.Suppliers.IntegrationTests;

/// <summary>
/// Where an IBAN could leak as plain text, read with Npgsql rather than through the service, which would decrypt it
/// on the way out and prove nothing.
/// </summary>
[Collection(SharedHost.Name)]
public sealed class IbanProtectionTests(SuppliersFixture fixture)
{
    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task Every_stored_iban_is_ciphertext_under_the_payment_data_key()
    {
        string approved = SupplierApi.NewIban();
        string pending = SupplierApi.NewIban();
        Guid id = (await _api.ActiveSupplierAsync(approved)).Id;
        await _api.ProposeAsync(TestUsers.Sam, id, pending);

        List<string> stored = await fixture.ColumnAsync(
            "select iban from bank_accounts where supplier_id = @id order by proposed_at", ("id", id));

        stored.Count.ShouldBe(2);
        stored.ShouldAllBe(value => value.StartsWith("v1.test.", StringComparison.Ordinal));
        string approvedAccount = approved[4..], pendingAccount = pending[4..];
        stored.ShouldAllBe(value => !value.Contains(approvedAccount) && !value.Contains(pendingAccount));
        stored.Select(SuppliersFixture.PaymentDataKey.Unprotect).ShouldBe([approved, pending]);
    }

    [Fact]
    public async Task No_outbox_message_ever_carries_the_iban_and_the_event_decrypts_to_it_with_the_shared_key()
    {
        string iban = SupplierApi.NewIban();
        Guid id = (await _api.ActiveSupplierAsync(iban)).Id;
        SupplierChanged published = await fixture.Probe.WaitForAsync<SupplierChanged>(
            message => message.SupplierId == id && message.Version == 1);

        List<string> bodies = await fixture.ColumnAsync(
            "select body from test_outbox_capture where body like '%' || @id || '%'", ("id", id.ToString()));

        bodies.ShouldHaveSingleItem();
        string accountNumber = iban[4..];
        bodies.ShouldAllBe(body => !body.Contains(accountNumber));

        VerifiedBankAccount account = published.BankAccount.ShouldNotBeNull();
        JsonSerializer.Serialize(published).ShouldNotContain(iban[4..]);
        SuppliersFixture.PaymentDataKey.Unprotect(account.ProtectedIban).ShouldBe(iban);
        account.IbanLastFour.ShouldBe(iban[^4..]);
    }
}
