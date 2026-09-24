namespace Matchbook.Budgets.Application;

/// <summary>
/// A create a client may repeat. Its id is the one the client sent, and it is saved with the request and the
/// response in the same transaction as the thing it created, so a repeat either finds it or finds nothing at all.
/// </summary>
/// <remarks>
/// Next to <see cref="IBudgetsDb"/> rather than in Common: it is one of the entities that interface exposes, and
/// the migrations know it by its full type name.
/// </remarks>
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
