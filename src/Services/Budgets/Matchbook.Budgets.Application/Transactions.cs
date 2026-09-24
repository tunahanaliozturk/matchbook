namespace Matchbook.Budgets.Application;

internal static class Transactions
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction, joining the one already open if there is one. A message
    /// consumer runs inside the transaction its outbox opened; an API request has none. Either way the balance
    /// UPDATE, the rows the change tracker saves afterwards and the outgoing events commit together or not at all.
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(
        this IBudgetsDb db, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        T result = await work();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public static Task InTransactionAsync(this IBudgetsDb db, Func<Task> work, CancellationToken cancellationToken) =>
        db.InTransactionAsync(
            async () =>
            {
                await work();
                return true;
            },
            cancellationToken);
}
