using System.Net;
using Matchbook.Contracts.Suppliers;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Api.Features.Suppliers;
using Matchbook.Testing;
using Npgsql;

namespace Matchbook.Suppliers.IntegrationTests;

/// <summary>
/// Requests that race each other against real Postgres. The in-memory rules cannot see a request running beside
/// them; the unique indexes and row versions can, and the loser has to get a 409 with a code, never a 500 or a
/// second row.
/// </summary>
[Collection(SharedHost.Name)]
public sealed class RaceTests(SuppliersFixture fixture)
{
    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task Creates_racing_on_one_tax_id_leave_one_supplier_and_refuse_the_rest_with_the_rule_code()
    {
        string taxId = SupplierApi.NewTaxId();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(attempt =>
            _api.PostAsync(TestUsers.Sam, "/suppliers", SupplierApi.NewSupplier(taxId, name: $"Acme {attempt}"))));

        responses.Count(static response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (HttpResponseMessage loser in responses.Where(static response => response.StatusCode != HttpStatusCode.Created))
        {
            await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, "supplier.tax_id_taken");
        }

        (await fixture.ScalarAsync<long>("select count(*) from suppliers where tax_id = @taxId", ("taxId", taxId)))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Proposals_racing_for_one_supplier_leave_one_pending_and_refuse_the_rest_with_the_rule_code()
    {
        Guid id = (await _api.ActiveSupplierAsync(SupplierApi.NewIban())).Id;

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            _api.PostAsync(TestUsers.Sam, $"/suppliers/{id}/bank-accounts", SupplierApi.BankAccount(SupplierApi.NewIban()))));

        responses.Count(static response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (HttpResponseMessage loser in responses.Where(static response => response.StatusCode != HttpStatusCode.Created))
        {
            await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, "supplier.bank_account_pending");
        }

        (await _api.GetAsync(TestUsers.Sam, id)).BankAccounts.Count(static account => account.Status == BankAccountStatus.Pending)
            .ShouldBe(1);
    }

    /// <summary>
    /// Both approvals read the proposal as pending before either writes. The test makes sure of that rather than
    /// hoping for it: it holds a lock on the supplier row, waits until Postgres reports both requests blocked on
    /// it, and only then lets go. The first to write wins; the second finds the row version changed under it.
    /// </summary>
    [Fact]
    public async Task Two_approvals_of_one_proposal_at_once_put_it_in_force_once_and_the_loser_gets_a_conflict()
    {
        Guid id = (await _api.ActiveSupplierAsync(SupplierApi.NewIban())).Id;
        Guid proposal = SupplierApi.PendingAccount(await _api.ProposeAsync(TestUsers.Sam, id, SupplierApi.NewIban()));
        Actor secondApprover = TestUsers.Stranger(Roles.SupplierApprover);

        HttpResponseMessage[] responses;
        await using (var connection = new NpgsqlConnection(fixture.Host.Database))
        {
            await connection.OpenAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await using (NpgsqlCommand lockRow = SuppliersFixture.Command(
                connection, "select 1 from suppliers where id = @id for update", ("id", id)))
            {
                await lockRow.ExecuteNonQueryAsync();
            }

            Task<HttpResponseMessage>[] approvals =
            [
                _api.PostAsync(TestUsers.Sofia, $"/suppliers/{id}/bank-accounts/{proposal}/approve"),
                _api.PostAsync(secondApprover, $"/suppliers/{id}/bank-accounts/{proposal}/approve"),
            ];

            await Eventually.MatchesAsync(
                () => fixture.ScalarAsync<long>(
                    "select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'"),
                static blocked => blocked == 2);

            await transaction.RollbackAsync();
            responses = await Task.WhenAll(approvals);
        }

        responses.Count(static response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        await responses.Single(static response => response.StatusCode != HttpStatusCode.OK)
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency.conflict");

        SupplierResponse supplier = await _api.GetAsync(TestUsers.Sam, id);
        supplier.AccountVersion.ShouldBe(2);
        supplier.Version.ShouldBe(2);
        await fixture.Probe.WaitForAsync<SupplierChanged>(message => message.SupplierId == id && message.Version == 2);
        await fixture.Probe.ShouldNotReceiveAsync<SupplierChanged>(
            message => message.SupplierId == id && message.Version > 2, TimeSpan.FromSeconds(1));
    }
}
