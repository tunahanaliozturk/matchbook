using System.Net;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Api;
using Matchbook.Testing;

namespace Matchbook.Suppliers.IntegrationTests;

/// <summary>A client that lost a response to a timeout sends the same create again, with the same id.</summary>
[Collection(SharedHost.Name)]
public sealed class IdempotencyTests(SuppliersFixture fixture)
{
    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task Repeating_a_create_with_its_id_answers_as_the_first_did_and_creates_nothing()
    {
        Guid id = Guid.NewGuid();
        object request = SupplierApi.NewSupplier(id: id);

        HttpResponseMessage first = await _api.PostAsync(TestUsers.Sam, "/suppliers", request);
        HttpResponseMessage again = await _api.PostAsync(TestUsers.Sam, "/suppliers", request);

        again.StatusCode.ShouldBe(first.StatusCode);
        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await again.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await fixture.ScalarAsync<long>("select count(*) from suppliers where id = @id", ("id", id))).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_id_with_different_content_or_from_someone_else_is_refused()
    {
        Guid id = Guid.NewGuid();
        string taxId = SupplierApi.NewTaxId();
        await _api.CreateAsync(TestUsers.Sam, SupplierApi.NewSupplier(taxId, id));

        await (await _api.PostAsync(TestUsers.Sam, "/suppliers", SupplierApi.NewSupplier(taxId, id, name: "Other GmbH")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
        await (await _api.PostAsync(TestUsers.Stranger(Roles.SupplierAdmin), "/suppliers", SupplierApi.NewSupplier(taxId, id)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }

    [Fact]
    public async Task Repeating_a_proposal_with_its_id_proposes_once()
    {
        Guid supplier = (await _api.CreateAsync(TestUsers.Sam)).Id;
        Guid proposal = Guid.NewGuid();
        string iban = SupplierApi.NewIban();
        string path = $"/suppliers/{supplier}/bank-accounts";

        HttpResponseMessage first = await _api.PostAsync(TestUsers.Sam, path, SupplierApi.BankAccount(iban, proposal));
        HttpResponseMessage again = await _api.PostAsync(TestUsers.Sam, path, SupplierApi.BankAccount(iban, proposal));

        (await SupplierApi.ReadAsync(first, HttpStatusCode.Created)).BankAccounts.ShouldHaveSingleItem().Id.ShouldBe(proposal);
        (await SupplierApi.ReadAsync(again, HttpStatusCode.Created)).BankAccounts.ShouldHaveSingleItem().Id.ShouldBe(proposal);
        await (await _api.PostAsync(TestUsers.Sam, path, SupplierApi.BankAccount(SupplierApi.NewIban(), proposal)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }

    [Fact]
    public async Task A_proposal_id_already_used_for_another_supplier_is_refused()
    {
        Guid proposal = Guid.NewGuid();
        Guid first = (await _api.CreateAsync(TestUsers.Sam)).Id;
        Guid second = (await _api.CreateAsync(TestUsers.Sam)).Id;
        await _api.PostAsync(TestUsers.Sam, $"/suppliers/{first}/bank-accounts", SupplierApi.BankAccount(SupplierApi.NewIban(), proposal));

        await (await _api.PostAsync(
                TestUsers.Sam, $"/suppliers/{second}/bank-accounts", SupplierApi.BankAccount(SupplierApi.NewIban(), proposal)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
    }
}
