namespace Matchbook.SharedKernel;

/// <summary>What kind of refusal a broken rule is, which the API turns into a status code.</summary>
public enum ViolationKind
{
    /// <summary>The request is malformed or breaks a rule on its own terms. 422.</summary>
    Invalid,

    /// <summary>The request is fine but the current state does not allow it. 409.</summary>
    Conflict,

    /// <summary>The thing the request is about does not exist. 404.</summary>
    NotFound,

    /// <summary>The actor is who they say, but may not do this. 403.</summary>
    Forbidden,
}

/// <summary>
/// A business rule said no. <see cref="Code"/> is stable and machine-readable (<c>requisition.self_approval</c>),
/// so a client branches on it rather than on the message, which is for people.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message, ViolationKind kind = ViolationKind.Conflict)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Kind = kind;
    }

    public BusinessRuleException()
        : this("business_rule", "A business rule was broken.")
    {
    }

    public BusinessRuleException(string message)
        : this("business_rule", message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "business_rule";
    }

    public string Code { get; }

    public ViolationKind Kind { get; }
}
