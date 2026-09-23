using System.Net;
using System.Text.Json;
using Shouldly;

namespace Matchbook.Testing;

public static class ProblemResponses
{
    /// <summary>
    /// Asserts the response is a problem with <paramref name="status"/> and the stable <paramref name="code"/>, the
    /// two things a client is entitled to depend on.
    /// </summary>
    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", body);

        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code, body);
    }
}
