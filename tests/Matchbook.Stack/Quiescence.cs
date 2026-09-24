using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Npgsql;

namespace Matchbook.Stack;

/// <summary>
/// Waits until nothing is in flight: every outbox table empty and every queue drained. Only then do the five
/// databases describe one state of the world, and only then can they be compared.
/// </summary>
public sealed class Quiescence(StackOptions options, HttpClient http)
{
    public static readonly string[] Databases = ["suppliers", "budgets", "requisitions", "purchasing", "payables"];

    /// <summary>
    /// Returns once three consecutive looks, half a second apart, found nothing pending, or throws with what was
    /// still pending when <paramref name="timeout"/> ran out.
    /// </summary>
    public async Task<QuietState> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        int quietLooks = 0;
        QuietState state = await LookAsync(cancellationToken);

        while (true)
        {
            quietLooks = state.Pending == 0 ? quietLooks + 1 : 0;

            if (quietLooks >= 3)
            {
                return state;
            }

            if (clock.Elapsed > timeout)
            {
                throw new TimeoutException($"Still in flight after {timeout}: {state}");
            }

            await Task.Delay(500, cancellationToken);
            state = await LookAsync(cancellationToken);
        }
    }

    public async Task<QuietState> LookAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, long> outboxes = [];

        foreach (string database in Databases)
        {
            await using var connection = new NpgsqlConnection(options.ReaderConnection(database));
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("select count(*) from outbox_message", connection);
            outboxes[database] = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.RabbitManagement, "queues"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.RabbitUser}:{options.RabbitPassword}")));

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        RabbitQueue[] queues = await response.Content.ReadFromJsonAsync<RabbitQueue[]>(cancellationToken) ?? [];

        Dictionary<string, long> working = queues
            .Where(static queue => !queue.Name.EndsWith("_error", StringComparison.Ordinal) && !queue.Name.EndsWith("_skipped", StringComparison.Ordinal))
            .Where(static queue => queue.Messages > 0)
            .ToDictionary(static queue => queue.Name, static queue => queue.Messages);

        Dictionary<string, long> errors = queues
            .Where(static queue => queue.Name.EndsWith("_error", StringComparison.Ordinal) && queue.Messages > 0)
            .ToDictionary(static queue => queue.Name, static queue => queue.Messages);

        return new QuietState(outboxes, working, errors);
    }

    private sealed record RabbitQueue(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("messages")] long Messages);
}

/// <summary>What was pending at one look: outbox rows per database, messages per working queue, and error queues.</summary>
public sealed record QuietState(
    IReadOnlyDictionary<string, long> Outboxes,
    IReadOnlyDictionary<string, long> Queues,
    IReadOnlyDictionary<string, long> ErrorQueues)
{
    public long Pending => Outboxes.Values.Sum() + Queues.Values.Sum();

    public override string ToString() =>
        $"outboxes [{string.Join(", ", Outboxes.Where(static pair => pair.Value > 0).Select(static pair => $"{pair.Key}={pair.Value}"))}], " +
        $"queues [{string.Join(", ", Queues.Select(static pair => $"{pair.Key}={pair.Value}"))}], " +
        $"errors [{string.Join(", ", ErrorQueues.Select(static pair => $"{pair.Key}={pair.Value}"))}]";
}
