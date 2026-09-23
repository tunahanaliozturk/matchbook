namespace Matchbook.SharedKernel;

/// <summary>
/// The person behind a request, as the identity provider vouched for them. Domain rules that care who acts,
/// separation of duties above all, take one of these rather than reaching for an HTTP context.
/// </summary>
public sealed record Actor(Guid Id, string Name, IReadOnlySet<string> Roles)
{
    public bool IsIn(string role) => Roles.Contains(role);
}
