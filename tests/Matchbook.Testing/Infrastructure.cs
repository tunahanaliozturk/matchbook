using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Matchbook.Testing;

/// <summary>
/// One Postgres and one RabbitMQ for a whole test assembly, started once. Each service host under test gets its own
/// database and its own RabbitMQ virtual host from them, so test classes never see each other's rows or messages
/// while the containers are paid for only once.
/// </summary>
/// <remarks>
/// Register it with <c>[assembly: AssemblyFixture(typeof(Infrastructure))]</c> and take it in a test class's
/// constructor.
/// </remarks>
public sealed class Infrastructure : IAsyncLifetime
{
    // Pinned by digest, like the compose stack, so a passing run today and a failing one next month cannot differ
    // only in which build of Postgres or RabbitMQ a moving tag happened to name.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3-alpine@sha256:2531fe16e1cb4ec4086d3eaa63118c8f074dd98620d55f022f453a397b18f037").Build();

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>Creates an empty database and returns a connection string to it.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        string name = $"{prefix}_{Guid.NewGuid():N}".ToLowerInvariant();

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"create database \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Creates an empty RabbitMQ virtual host and returns an AMQP address for it.</summary>
    public async Task<Uri> CreateVirtualHostAsync(string prefix)
    {
        string name = $"{prefix}-{Guid.NewGuid():N}";

        await RabbitCtlAsync("add_vhost", name);
        await RabbitCtlAsync("set_permissions", "-p", name, RabbitMqBuilder.DefaultUsername, ".*", ".*", ".*");

        var broker = new UriBuilder(_rabbit.GetConnectionString()) { Path = "/" + Uri.EscapeDataString(name) };
        return broker.Uri;
    }

    private async Task RabbitCtlAsync(params string[] arguments)
    {
        DotNet.Testcontainers.Containers.ExecResult result = await _rabbit.ExecAsync(["rabbitmqctl", .. arguments]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rabbitmqctl {string.Join(' ', arguments)} failed: {result.Stderr}");
        }
    }
}
