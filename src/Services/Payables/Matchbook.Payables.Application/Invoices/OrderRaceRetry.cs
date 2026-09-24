using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Payables.Application.Invoices;

/// <summary>
/// Runs an invoice command again when it loses the race for its order's row. Capturing an invoice and an approver
/// releasing one both claim the order (see <see cref="InvoiceMatcher.ClaimOrderAsync"/>), and so do the consumers of
/// receipts and order events. A consumer that loses is retried by MassTransit. A person should not have to be: the
/// conflict is on a row they never read, and a 409 telling them "someone else changed this since you read it" would
/// be wrong. None of these commands takes a version from the client, so running it again is always what they meant.
/// </summary>
/// <remarks>
/// Each attempt gets its own scope, so a fresh DbContext and a fresh outbox. Clearing the change tracker instead
/// would leave the outbox's scoped state believing it had already written rows that were rolled back. The losing
/// transaction has rolled back entirely by the time the exception arrives, and the winner has committed, so the
/// next attempt reads the order as it now is.
/// </remarks>
public sealed class OrderRaceRetry(IServiceScopeFactory scopes)
{
    // Each loss means another writer committed on the same order in between. Three in a row would take a burst of
    // receipts on one order that nothing here produces; past that the 409 is the honest answer.
    private const int Attempts = 3;

    public async Task<TResult> RunAsync<THandler, TResult>(Func<THandler, Task<TResult>> command)
        where THandler : notnull
    {
        ArgumentNullException.ThrowIfNull(command);

        for (int attempt = 1; ; attempt++)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            try
            {
                return await command(scope.ServiceProvider.GetRequiredService<THandler>());
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
            }
        }
    }
}
