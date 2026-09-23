namespace Matchbook.BuildingBlocks.Persistence;

/// <summary>Tags that decide which health endpoint runs a check.</summary>
public static class HealthTags
{
    /// <summary>Checks that must pass before the instance takes traffic: its database and its broker.</summary>
    public const string Ready = "ready";
}
