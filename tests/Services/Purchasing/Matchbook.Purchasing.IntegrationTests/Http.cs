using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matchbook.Purchasing.IntegrationTests;

internal static class Routes
{
    public static Uri Orders(string query = "") => Relative($"/purchase-orders{query}");

    public static Uri Order(Guid id) => Relative($"/purchase-orders/{id}");

    public static Uri ForRequisition(Guid requisitionId) => Relative($"/purchase-orders/by-requisition/{requisitionId}");

    public static Uri Line(Guid id, int lineNumber) => Relative($"/purchase-orders/{id}/lines/{lineNumber}");

    public static Uri Issue(Guid id) => Relative($"/purchase-orders/{id}/issue");

    public static Uri ShortClose(Guid id) => Relative($"/purchase-orders/{id}/short-close");

    public static Uri Cancel(Guid id) => Relative($"/purchase-orders/{id}/cancel");

    public static Uri Receipts(Guid id) => Relative($"/purchase-orders/{id}/receipts");

    public static Uri Receipt(Guid id, Guid receiptId) => Relative($"/purchase-orders/{id}/receipts/{receiptId}");

    private static Uri Relative(string path) => new(path, UriKind.Relative);
}

internal static class Http
{
    /// <summary>The service's JSON conventions: web defaults with enums as their names.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Asserts the status, showing the body when it is wrong, and reads the body.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode status)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, Uri uri, T body) =>
        client.PostAsJsonAsync(uri, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, Uri uri, T body) =>
        client.PutAsJsonAsync(uri, body, Json);
}
