namespace Matchbook.Stack;

/// <summary>Where the compose stack listens, as <c>compose.yaml</c> publishes it.</summary>
public sealed record StackOptions
{
    public Uri Gateway { get; init; } = new("http://localhost:5300/api/");

    public Uri Keycloak { get; init; } = new("http://localhost:8080/realms/matchbook/");

    public Uri RabbitManagement { get; init; } = new("http://localhost:15672/api/");

    public string RabbitUser { get; init; } = "matchbook";

    public string RabbitPassword { get; init; } = "matchbook";

    public string DatabaseHost { get; init; } = "localhost";

    public int DatabasePort { get; init; } = 5440;

    public string Password { get; init; } = "matchbook";

    /// <summary>A connection to one service's database as the read-only reconciler role.</summary>
    public string ReaderConnection(string database) =>
        $"Host={DatabaseHost};Port={DatabasePort};Database={database};Username=reconciler;Password=reconciler;Pooling=false";
}
