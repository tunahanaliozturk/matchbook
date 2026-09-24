namespace Matchbook.Requisitions.Domain;

/// <summary>Who has to sign a step off.</summary>
public enum ApprovalStepKind
{
    /// <summary>The cost centre's manager on record when the route was fixed, and nobody else.</summary>
    Manager,

    /// <summary>Anyone holding the <c>finance-approver</c> role.</summary>
    Finance,

    /// <summary>Anyone holding the <c>cfo</c> role.</summary>
    Cfo,
}

/// <summary>Which steps a requisition of a given amount has to pass, in order.</summary>
public static class ApprovalRoute
{
    /// <summary>An amount strictly above this also needs a finance approver.</summary>
    public const decimal FinanceThreshold = 10_000m;

    /// <summary>An amount strictly above this also needs the CFO.</summary>
    public const decimal CfoThreshold = 100_000m;

    public static IReadOnlyList<ApprovalStepKind> For(decimal amount) =>
        amount > CfoThreshold ? [ApprovalStepKind.Manager, ApprovalStepKind.Finance, ApprovalStepKind.Cfo]
        : amount > FinanceThreshold ? [ApprovalStepKind.Manager, ApprovalStepKind.Finance]
        : [ApprovalStepKind.Manager];
}
