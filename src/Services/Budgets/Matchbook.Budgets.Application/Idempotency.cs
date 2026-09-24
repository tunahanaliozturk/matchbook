using System.Text.Json;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application;

/// <summary>
/// A create a client may repeat. Its id is the one the client sent, and it is saved with the request and the
/// response in the same transaction as the thing it created, so a repeat either finds it or finds nothing at all.
/// </summary>
public sealed class IdempotentRequest
{
    private IdempotentRequest(Guid id, string operation, string request, string response, DateTimeOffset recordedAt)
    {
        Id = id;
        Operation = operation;
        Request = request;
        Response = response;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>The command's type name, so one id cannot be reused across two kinds of create.</summary>
    public string Operation { get; private set; }

    public string Request { get; private set; }

    public string Response { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    internal static IdempotentRequest Record(Guid id, string operation, string request, string response, DateTimeOffset recordedAt) =>
        new(id, operation, request, response, recordedAt.ToUniversalTime());
}

internal static class Idempotency
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The response the first request with this id produced, or null when there was none. The same id with
    /// different content is refused: that is a client bug, and answering it with the first result would hide it.
    /// </summary>
    /// <remarks>
    /// Commands are compared as records, not as stored text, so 100 and 100.00 are the same amount.
    /// </remarks>
    public static async Task<TResult?> ReplayAsync<TCommand, TResult>(
        this IBudgetsDb db, Guid? id, TCommand command, CancellationToken cancellationToken)
        where TCommand : class
        where TResult : class
    {
        if (id is not { } requestId)
        {
            return null;
        }

        IdempotentRequest? earlier = await db.Requests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (earlier is null)
        {
            return null;
        }

        if (earlier.Operation != typeof(TCommand).Name || !command.Equals(JsonSerializer.Deserialize<TCommand>(earlier.Request, Json)))
        {
            throw new BusinessRuleException(
                "request.id_reused",
                $"Request id {requestId} was already used for a different request. Send a new id for a new request.");
        }

        return JsonSerializer.Deserialize<TResult>(earlier.Response, Json);
    }

    /// <summary>Adds the record to the unit of work; the caller's save commits it with the create.</summary>
    public static void Remember<TCommand, TResult>(this IBudgetsDb db, Guid? id, TCommand command, TResult result, DateTimeOffset now)
    {
        if (id is { } requestId)
        {
            db.Requests.Add(IdempotentRequest.Record(
                requestId,
                typeof(TCommand).Name,
                JsonSerializer.Serialize(command, Json),
                JsonSerializer.Serialize(result, Json),
                now));
        }
    }
}
