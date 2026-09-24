using System.Net;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Api.Features.Suppliers;
using Matchbook.Testing;
using SupplierChanged = Matchbook.Contracts.Suppliers.SupplierChanged;

namespace Matchbook.Suppliers.IntegrationTests;

[Collection(SharedHost.Name)]
public sealed class FourEyesTests(SuppliersFixture fixture)
{
    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task A_supplier_goes_live_only_through_a_second_person_and_each_change_is_published_as_the_next_version()
    {
        SupplierResponse supplier = await _api.CreateAsync(TestUsers.Sam);
        Guid id = supplier.Id;
        Guid first = SupplierApi.PendingAccount(await _api.ProposeAsync(TestUsers.Sam, id, SupplierApi.NewIban()));
        await _api.DoAsync(TestUsers.Sam, id, "submit");

        await (await _api.PostAsync(TestUsers.Sam, $"/suppliers/{id}/activate"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "supplier.role_required");
        await (await _api.PostAsync(TestUsers.Sam, $"/suppliers/{id}/bank-accounts/{first}/approve"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "supplier.role_required");
        await (await _api.PostAsync(TestUsers.Sofia, $"/suppliers/{id}/activate"))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "supplier.no_verified_account");

        // Nothing is published before activation, so the approval of the first account is not an event.
        await _api.DoAsync(TestUsers.Sofia, id, $"bank-accounts/{first}/approve");

        // Each step waits for its event, so the order observed is the order of the changes, not of the broker.
        (await _api.DoAsync(TestUsers.Sofia, id, "activate")).Status.ShouldBe(SupplierStatus.Active);
        SupplierChanged activated = await VersionAsync(id, 1);

        await _api.DoAsync(TestUsers.Sam, id, "block", new { reason = "Bank letter under review" });
        SupplierChanged blocked = await VersionAsync(id, 2);

        Guid second = SupplierApi.PendingAccount(await _api.ProposeAsync(TestUsers.Sam, id, SupplierApi.NewIban()));
        await _api.DoAsync(TestUsers.Sofia, id, $"bank-accounts/{second}/approve");
        SupplierChanged newAccount = await VersionAsync(id, 3);

        (activated.Status, activated.BankAccount!.AccountVersion).ShouldBe(("Active", 1));
        (blocked.Status, blocked.BankAccount!.AccountVersion).ShouldBe(("Blocked", 1));
        (newAccount.Status, newAccount.BankAccount!.AccountVersion).ShouldBe(("Blocked", 2));
        fixture.Probe.Received<SupplierChanged>().Where(message => message.SupplierId == id)
            .Select(static message => message.Version).ShouldBe([1L, 2L, 3L]);
        fixture.Probe.Received<SupplierChanged>().Where(message => message.SupplierId == id)
            .ShouldAllBe(message => message.OccurredAt.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task Holding_both_roles_does_not_let_anyone_approve_their_own_account_or_activate_their_own_submission()
    {
        Actor both = TestUsers.Stranger(Roles.SupplierAdmin, Roles.SupplierApprover);
        Guid id = (await _api.CreateAsync(both)).Id;
        Guid account = SupplierApi.PendingAccount(await _api.ProposeAsync(both, id, SupplierApi.NewIban()));

        await (await _api.PostAsync(both, $"/suppliers/{id}/bank-accounts/{account}/approve"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "supplier.self_approval");
        await _api.DoAsync(TestUsers.Sofia, id, $"bank-accounts/{account}/approve");

        await _api.DoAsync(both, id, "submit");
        await (await _api.PostAsync(both, $"/suppliers/{id}/activate"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "supplier.self_approval");

        (await _api.DoAsync(TestUsers.Sofia, id, "activate")).ActivatedBy.ShouldBe(TestUsers.Sofia.Id);
    }

    private Task<SupplierChanged> VersionAsync(Guid supplierId, long version) =>
        fixture.Probe.WaitForAsync<SupplierChanged>(message => message.SupplierId == supplierId && message.Version == version);
}
