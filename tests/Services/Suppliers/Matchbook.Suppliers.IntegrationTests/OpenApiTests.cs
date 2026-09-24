using System.Net;
using System.Text.Json;

namespace Matchbook.Suppliers.IntegrationTests;

/// <summary>A client is generated from this document, so a missing response type is a missing type in the client.</summary>
[Collection(SharedHost.Name)]
public sealed class OpenApiTests(SuppliersFixture fixture)
{
    [Fact]
    public async Task The_document_describes_every_endpoint_with_its_body_and_its_problems()
    {
        HttpResponseMessage response = await fixture.Host.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        List<(string Path, string Method, JsonElement Operation)> operations =
        [
            .. document.RootElement.GetProperty("paths").EnumerateObject()
                .SelectMany(static path => path.Value.EnumerateObject()
                    .Select(operation => (path.Name, operation.Name, operation.Value))),
        ];

        operations.Select(static operation => $"{operation.Method} {operation.Path}").ShouldBe(
        [
            "get /suppliers",
            "post /suppliers",
            "get /suppliers/{supplierId}",
            "put /suppliers/{supplierId}",
            "post /suppliers/{supplierId}/submit",
            "post /suppliers/{supplierId}/activate",
            "post /suppliers/{supplierId}/block",
            "post /suppliers/{supplierId}/unblock",
            "post /suppliers/{supplierId}/bank-accounts",
            "post /suppliers/{supplierId}/bank-accounts/{bankAccountId}/approve",
            "post /suppliers/{supplierId}/bank-accounts/{bankAccountId}/reject",
        ], ignoreOrder: true);

        foreach ((string path, string method, JsonElement operation) in operations)
        {
            string name = $"{method} {path}";
            operation.GetProperty("operationId").GetString().ShouldNotBeNullOrEmpty(name);
            JsonElement responses = operation.GetProperty("responses");
            JsonElement success = responses.EnumerateObject().Single(static status => status.Name.StartsWith('2')).Value;
            success.GetProperty("content").GetProperty("application/json").GetProperty("schema").ValueKind
                .ShouldBe(JsonValueKind.Object, name);

            foreach (string refusal in (string[])["401", "403"])
            {
                responses.GetProperty(refusal).GetProperty("content").TryGetProperty("application/problem+json", out _)
                    .ShouldBeTrue($"{name} {refusal}");
            }

            if (operation.TryGetProperty("requestBody", out JsonElement body))
            {
                body.GetProperty("content").GetProperty("application/json").GetProperty("schema").ValueKind
                    .ShouldBe(JsonValueKind.Object, name);
                responses.TryGetProperty("400", out _).ShouldBeTrue($"{name} has a body and so can be malformed");
            }
        }

        document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails")
            .GetProperty("properties").TryGetProperty("code", out _).ShouldBeTrue();
    }
}
