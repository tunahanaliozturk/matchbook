using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Matchbook.SharedKernel;
using Matchbook.Stack;

namespace Matchbook.SystemTests;

/// <summary>
/// The stack as its users meet it: a token from Keycloak per person, every request through the gateway, and the
/// patience a real client has while a container restarts behind it. Nothing here knows a service's types; the
/// answers are read as JSON, the way a client written in any language would read them.
/// </summary>
internal sealed class Gateway(StackOptions options, HttpClient http) : IDisposable
{
    // Long enough to outlast a killed service coming back and running its migrations check, short enough that a
    // service which never comes back fails the test rather than hanging it.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> tokens = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim signIn = new(1, 1);

    public Task<JsonElement> GetAsync(Actor actor, string path) => SendAsync(actor, HttpMethod.Get, path, body: null);

    public Task<JsonElement> PostAsync(Actor actor, string path, object? body = null) => SendAsync(actor, HttpMethod.Post, path, body);

    /// <summary>The answer, or null when the gateway says the thing does not exist (yet).</summary>
    public async Task<JsonElement?> FindAsync(Actor actor, string path)
    {
        try
        {
            return await GetAsync(actor, path);
        }
        catch (ProblemException problem) when (problem.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> GetTextAsync(Actor actor, string path)
    {
        using HttpResponseMessage response = await SendWithRetriesAsync(actor, HttpMethod.Get, path, body: null);
        await ThrowIfRefusedAsync(response);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// A lost race is answered 409 <c>concurrency.conflict</c> after the service rolled its transaction back, so
    /// nothing happened and the request is sent again, as the answer asks. Payables retries its own order races
    /// before answering, so this is the client's share of the contract rather than a workaround for one service.
    /// </summary>
    private async Task<JsonElement> SendAsync(Actor actor, HttpMethod method, string path, object? body)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpResponseMessage response = await SendWithRetriesAsync(actor, method, path, body);

            try
            {
                await ThrowIfRefusedAsync(response);
            }
            catch (ProblemException problem) when (problem.Code == "concurrency.conflict" && attempt < 5)
            {
                await Task.Delay(100 * attempt);
                continue;
            }

            return response.StatusCode == HttpStatusCode.NoContent
                ? default
                : await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }

    /// <summary>
    /// Sends, and sends again while the failure is one a restart causes: no connection at all, or the gateway
    /// answering 502 to 504 because nobody is listening behind it. A request whose first attempt did land is then
    /// repeated, which is exactly the case client-chosen ids and <see cref="Steps.OnceAsync"/> exist for. A 429 is
    /// waited out the same way: thirty purchases polling as the same buyer run past the gateway's 200 requests a
    /// second per caller, and backing off is what a well-behaved client does.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetriesAsync(Actor actor, HttpMethod method, string path, object? body)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (true)
        {
            using var request = new HttpRequestMessage(method, new Uri(options.Gateway, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(actor));

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            try
            {
                HttpResponseMessage response = await http.SendAsync(request);

                if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
                        or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                    || DateTimeOffset.UtcNow > deadline)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (DateTimeOffset.UtcNow <= deadline)
            {
            }

            await Task.Delay(250);
        }
    }

    public void Dispose() => signIn.Dispose();

    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string text = await response.Content.ReadAsStringAsync();
        string? code = null;

        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            using var problem = JsonDocument.Parse(text);
            code = problem.RootElement.TryGetProperty("code", out JsonElement value) ? value.GetString() : null;
        }

        throw new ProblemException(response.StatusCode, code, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {text}");
    }

    private async Task<string> TokenAsync(Actor actor)
    {
        if (tokens.TryGetValue(actor.Name, out var cached) && cached.Expires > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cached.Token;
        }

        // One sign-in at a time. The realm has brute force protection, which counts logins by one user less than a
        // second apart as an attack and locks the account, so thirty purchases must not all sign bruno in at once.
        await signIn.WaitAsync();

        try
        {
            return tokens.TryGetValue(actor.Name, out cached) && cached.Expires > DateTimeOffset.UtcNow.AddMinutes(1)
                ? cached.Token
                : await SignInAsync(actor);
        }
        finally
        {
            signIn.Release();
        }
    }

    private async Task<string> SignInAsync(Actor actor)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "matchbook-cli",
            ["username"] = actor.Name,
            ["password"] = "matchbook",
        });

        using HttpResponseMessage response = await http.PostAsync(new Uri(options.Keycloak, "protocol/openid-connect/token"), form);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Keycloak refused to sign {actor.Name} in: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        JsonElement answer = await response.Content.ReadFromJsonAsync<JsonElement>();

        string token = answer.GetProperty("access_token").GetString()!;
        tokens[actor.Name] = (token, DateTimeOffset.UtcNow.AddSeconds(answer.GetProperty("expires_in").GetInt32()));
        return token;
    }
}

/// <summary>A refusal from a service: the HTTP status and the stable <c>code</c> from its problem details.</summary>
internal sealed class ProblemException(HttpStatusCode status, string? code, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;
}

internal static class Json
{
    public static string Text(this JsonElement element, string name) => element.GetProperty(name).GetString()!;

    public static Guid Id(this JsonElement element, string name = "id") => element.GetProperty(name).GetGuid();

    public static decimal Number(this JsonElement element, string name) => element.GetProperty(name).GetDecimal();
}
