using Matchbook.Testing;
using Npgsql;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// Makes two requests collide on purpose instead of hoping they do. The test takes a lock the requests will need,
/// starts them, waits until Postgres shows every one of them blocked on it, and lets go: they then contend at exactly
/// the statement under test, every run.
/// </summary>
internal static class Race
{
    public static async Task<T[]> AtAsync<T>(string database, string lockStatement, params Func<Task<T>>[] contenders)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using NpgsqlTransaction holder = await connection.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand(lockStatement, connection, holder))
        {
            await take.ExecuteNonQueryAsync();
        }

        Task<T>[] running = [.. contenders.Select(static contender => Task.Run(contender))];
        await Eventually.MatchesAsync(() => WaitingOnLocksAsync(database), waiting => waiting >= contenders.Length);
        await holder.RollbackAsync();

        return await Task.WhenAll(running);
    }

    private static async Task<long> WaitingOnLocksAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'",
            connection);
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
