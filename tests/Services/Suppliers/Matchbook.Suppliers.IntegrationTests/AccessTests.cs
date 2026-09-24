using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.Suppliers.Api;
using Matchbook.Testing;

namespace Matchbook.Suppliers.IntegrationTests;

[Collection(SharedHost.Name)]
public sealed class AccessTests(SuppliersFixture fixture)
{
    private static readonly Uri Suppliers = new("/suppliers", UriKind.Relative);

    private readonly SupplierApi _api = fixture.Api;

    [Fact]
    public async Task A_request_without_a_token_is_unauthorised()
    {
        HttpClient anonymous = fixture.Host.CreateClient();

        (await anonymous.GetAsync(Suppliers)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Suppliers, SupplierApi.NewSupplier())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Someone_outside_the_supplier_roles_is_stopped_before_any_handler_runs()
    {
        await StoppedAtTheDoorAsync(await _api.As(TestUsers.Bruno).GetAsync(Suppliers));
        await StoppedAtTheDoorAsync(await _api.PostAsync(TestUsers.Bruno, "/suppliers", SupplierApi.NewSupplier()));
    }

    [Fact]
    public async Task An_auditor_reads_everything_and_is_stopped_before_any_change()
    {
        SupplierResponse supplier = await _api.CreateAsync(TestUsers.Sam);

        (await _api.GetAsync(TestUsers.Audrey, supplier.Id)).Id.ShouldBe(supplier.Id);
        (await _api.As(TestUsers.Audrey).GetAsync(Suppliers)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await StoppedAtTheDoorAsync(await _api.PostAsync(TestUsers.Audrey, "/suppliers", SupplierApi.NewSupplier()));
        await StoppedAtTheDoorAsync(await _api.PostAsync(TestUsers.Audrey, $"/suppliers/{supplier.Id}/submit"));
    }

    [Fact]
    public async Task A_supplier_approver_creating_a_supplier_is_told_which_role_it_takes() =>
        await (await _api.PostAsync(TestUsers.Sofia, "/suppliers", SupplierApi.NewSupplier()))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "supplier.role_required");

    [Fact]
    public async Task A_request_missing_fields_is_a_400_naming_them()
    {
        HttpResponseMessage response = await _api.PostAsync(TestUsers.Sam, "/suppliers", new { legalName = "Acme GmbH" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").EnumerateObject().Select(static field => field.Name)
            .ShouldBe(["TaxId", "CountryCode", "PaymentTermsDays", "ContactEmail"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_value_the_domain_refuses_is_a_422_with_its_code() =>
        await (await _api.PostAsync(TestUsers.Sam, $"/suppliers/{(await _api.CreateAsync(TestUsers.Sam)).Id}/bank-accounts",
                SupplierApi.BankAccount("DE88370400440532013000")))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "supplier.iban_invalid");

    [Fact]
    public async Task An_unknown_supplier_is_a_404_with_its_code() =>
        await (await _api.As(TestUsers.Sofia).GetAsync(new Uri($"/suppliers/{Guid.NewGuid()}", UriKind.Relative)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "supplier.not_found");

    [Fact]
    public async Task A_created_supplier_can_be_read_back_at_its_location()
    {
        HttpResponseMessage created = await _api.PostAsync(TestUsers.Sam, "/suppliers", SupplierApi.NewSupplier());
        var location = new Uri(new Uri("http://localhost/suppliers"), created.Headers.Location!);

        (await _api.As(TestUsers.Sam).GetAsync(location)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The domain would refuse these too, with supplier.role_required. A 403 without a code shows the policy
    // refused first, so a handler never ran for someone the endpoint does not admit.
    private static async Task StoppedAtTheDoorAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        body.ShouldNotContain("\"code\"");
    }
}
