using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Domain;

public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>One sign-off on a requisition's route, numbered from 1 in the order it has to be taken.</summary>
public sealed class ApprovalStep
{
    public const int MaxSteps = 3;

    internal ApprovalStep(int sequence, ApprovalStepKind kind, Guid? approverId)
    {
        Sequence = sequence;
        Kind = kind;
        ApproverId = approverId;
    }

    public int Sequence { get; private set; }

    public ApprovalStepKind Kind { get; private set; }

    /// <summary>The manager on record for a <see cref="ApprovalStepKind.Manager"/> step; null for a role step.</summary>
    public Guid? ApproverId { get; private set; }

    public ApprovalDecision Decision { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>The realm role a step of this kind needs, or null when it is bound to one person.</summary>
    public string? RequiredRole => Kind switch
    {
        ApprovalStepKind.Finance => Roles.FinanceApprover,
        ApprovalStepKind.Cfo => Roles.Cfo,
        _ => null,
    };

    /// <summary>
    /// Whether the actor is the kind of person this step waits for. Separation of duties across steps is the
    /// requisition's business, not the step's.
    /// </summary>
    internal bool IsFor(Actor actor) =>
        RequiredRole is { } role ? actor.IsIn(role) : actor.Id == ApproverId;

    internal void Decide(ApprovalDecision decision, Guid decidedBy, DateTimeOffset now)
    {
        Decision = decision;
        DecidedBy = decidedBy;
        DecidedAt = now;
    }
}
