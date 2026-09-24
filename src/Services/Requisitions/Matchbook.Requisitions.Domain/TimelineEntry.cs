namespace Matchbook.Requisitions.Domain;

public enum TimelineAction
{
    Created,
    Edited,
    Submitted,
    FundsReserved,
    FundsRefused,
    StepApproved,
    Approved,
    Rejected,
    Cancelled,
    Ordered,
    Closed,
}

/// <summary>
/// Who did what to a requisition, and when. <see cref="Sequence"/> is the order this service learned of it,
/// which for facts reported by other services can differ from the order of <see cref="At"/>.
/// </summary>
public sealed class TimelineEntry
{
    public const int MaxActorNameLength = 200;

    internal TimelineEntry(
        Guid id,
        int sequence,
        DateTimeOffset at,
        TimelineAction action,
        Guid? actorId,
        string? actorName,
        string? detail)
    {
        Id = id;
        Sequence = sequence;
        At = at;
        Action = action;
        ActorId = actorId;
        ActorName = actorName;
        Detail = detail;
    }

    public Guid Id { get; private set; }

    public int Sequence { get; private set; }

    public DateTimeOffset At { get; private set; }

    public TimelineAction Action { get; private set; }

    /// <summary>Null when another service reported the fact and named nobody.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>The actor's name as the identity provider gave it at the time; null for another service.</summary>
    public string? ActorName { get; private set; }

    public string? Detail { get; private set; }
}
