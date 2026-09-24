using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Api;
using Matchbook.Testing;

namespace Matchbook.Suppliers.IntegrationTests;

/// <summary>
/// The service's HTTP API as the tests call it, so a test reads as the steps a person takes. Every call goes
/// through the real host: authentication, policies, validation, the handlers, Postgres and the outbox.
/// </summary>
public sealed class SupplierApi(ServiceHost<Program> host)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient As(Actor actor) => host.ClientFor(actor);

    public static object NewSupplier(string? taxId = null, Guid? id = null, string name = "Acme GmbH") => new
    {
        id,
        legalName = name,
        taxId = taxId ?? NewTaxId(),
        countryCode = "DE",
        paymentTermsDays = 30,
        contactEmail = "ap@acme.example",
    };

    public static string NewTaxId() =>
        string.Create(CultureInfo.InvariantCulture, $"DE{Random.Shared.NextInt64(100_000_000, 1_000_000_000)}");

    /// <summary>A German IBAN no other test uses, with check digits computed the long way.</summary>
    public static string NewIban()
    {
        string bban = string.Create(
            CultureInfo.InvariantCulture, $"{Random.Shared.NextInt64(10_000_000, 100_000_000)}{Random.Shared.NextInt64(1_000_000_000, 10_000_000_000)}");
        BigInteger rearranged = BigInteger.Parse(bban + "131400", CultureInfo.InvariantCulture); // D = 13, E = 14.
        return string.Create(CultureInfo.InvariantCulture, $"DE{98 - (int)(rearranged % 97):00}{bban}");
    }

    public static object BankAccount(string iban, Guid? id = null) =>
        new { id, iban, bic = "DEUTDEFF", accountHolder = "Acme GmbH" };

    public Task<HttpResponseMessage> PostAsync(Actor actor, string path, object? body = null) =>
        As(actor).PostAsJsonAsync(new Uri(path, UriKind.Relative), body ?? new { }, Json);

    public async Task<SupplierResponse> CreateAsync(Actor actor, object? supplier = null) =>
        await ReadAsync(await PostAsync(actor, "/suppliers", supplier ?? NewSupplier()), HttpStatusCode.Created);

    public async Task<SupplierResponse> GetAsync(Actor actor, Guid supplierId) =>
        await ReadAsync(await As(actor).GetAsync(new Uri($"/suppliers/{supplierId}", UriKind.Relative)), HttpStatusCode.OK);

    public async Task<SupplierResponse> ProposeAsync(Actor actor, Guid supplierId, string iban) =>
        await ReadAsync(
            await PostAsync(actor, $"/suppliers/{supplierId}/bank-accounts", BankAccount(iban)), HttpStatusCode.Created);

    public async Task<SupplierResponse> DoAsync(Actor actor, Guid supplierId, string action, object? body = null) =>
        await ReadAsync(await PostAsync(actor, $"/suppliers/{supplierId}/{action}", body), HttpStatusCode.OK);

    /// <summary>Created by sam, account proposed by sam and approved by sofia, submitted by sam, activated by sofia.</summary>
    public async Task<SupplierResponse> ActiveSupplierAsync(string iban)
    {
        SupplierResponse supplier = await CreateAsync(TestUsers.Sam);
        Guid account = (await ProposeAsync(TestUsers.Sam, supplier.Id, iban)).BankAccounts.Single().Id;
        await DoAsync(TestUsers.Sofia, supplier.Id, $"bank-accounts/{account}/approve");
        await DoAsync(TestUsers.Sam, supplier.Id, "submit");
        return await DoAsync(TestUsers.Sofia, supplier.Id, "activate");
    }

    public static Guid PendingAccount(SupplierResponse supplier) =>
        supplier.BankAccounts.Single(account => account.Status == BankAccountStatus.Pending).Id;

    public static async Task<SupplierResponse> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<SupplierResponse>(body, Json)!;
    }
}
