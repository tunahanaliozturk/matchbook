using Matchbook.BuildingBlocks.Security;
using Matchbook.Contracts.Suppliers;
using Matchbook.Testing;
using Npgsql;

[assembly: AssemblyFixture(typeof(Matchbook.Testing.Infrastructure))]

namespace Matchbook.Suppliers.IntegrationTests;

/// <summary>
/// Every test class in the assembly shares one host and one probe: starting the service is the expensive part,
/// and each test works on suppliers it created itself, with its own tax ids and IBANs.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedHost : ICollectionFixture<SuppliersFixture>
{
    public const string Name = "suppliers";
}

public sealed class SuppliersFixture(Matchbook.Testing.Infrastructure infrastructure) : IAsyncLifetime
{
    /// <summary>The payment-data key the host runs with (<see cref="ServiceHost"/> configures id "test", all zeros).</summary>
    public static readonly ColumnProtector PaymentDataKey =
        new(new Dictionary<string, byte[]> { ["test"] = new byte[32] }, "test");

    public ServiceHost<Program> Host { get; private set; } = null!;

    public EventProbe Probe { get; private set; } = null!;

    public SupplierApi Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = await ServiceHost.StartAsync<Program>(infrastructure, "suppliers");
        await CaptureOutboxAsync();
        Probe = await EventProbe.StartAsync(Host.Broker, static listen => listen.For<SupplierChanged>());
        Api = new SupplierApi(Host);
    }

    public async ValueTask DisposeAsync()
    {
        await Probe.DisposeAsync();
        await Host.DisposeAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Host.Database);
        await connection.OpenAsync();
        await using NpgsqlCommand command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task<List<string>> ColumnAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Host.Database);
        await connection.OpenAsync();
        await using NpgsqlCommand command = Command(connection, sql, parameters);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<string> values = [];
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public static NpgsqlCommand Command(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(connection);

        NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    // The delivery service deletes outbox rows once the broker has them, so reading the table afterwards could
    // pass on an empty table. A trigger keeps a copy of every message body the service ever writes to it.
    private async Task CaptureOutboxAsync()
    {
        await using var connection = new NpgsqlConnection(Host.Database);
        await connection.OpenAsync();
        await using NpgsqlCommand command = Command(connection, """
            create table test_outbox_capture (body text not null);
            create function test_capture_outbox() returns trigger language plpgsql as $$
            begin
                insert into test_outbox_capture (body) values (new.body);
                return new;
            end $$;
            create trigger test_capture_outbox after insert on outbox_message
                for each row execute function test_capture_outbox();
            """);
        await command.ExecuteNonQueryAsync();
    }
}
