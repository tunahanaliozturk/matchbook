using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.BuildingBlocks.Http;

/// <summary>
/// What each unique index means to a client. When two requests race to create the same thing, the database is
/// the one that says no, and the loser should get the same 409 and code as if the application had noticed first.
/// </summary>
public sealed class UniqueViolations
{
    internal Dictionary<string, (string Code, string Message)> ByConstraint { get; } = new(StringComparer.Ordinal);
}

public static class UniqueViolationExtensions
{
    /// <summary>Answers a violation of <paramref name="constraint"/> with 409 and <paramref name="code"/>.</summary>
    public static IServiceCollection MapUniqueViolation(this IServiceCollection services, string constraint, string code, string message) =>
        services.Configure<UniqueViolations>(violations => violations.ByConstraint[constraint] = (code, message));
}
