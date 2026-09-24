using System.Diagnostics;
using System.Text.Json;
using Matchbook.Stack;
using Matchbook.Testing;

namespace Matchbook.SystemTests;

public sealed class ChaosTests(StackFixture stack, ITestOutputHelper output)
{
    /// <summary>
    /// Thirty purchases start half a second apart. While they are in flight the broker restarts, then Budgets and
    /// Payables, the two services that hold money, are each killed without warning and started again. Every
    /// purchase still has to reach payment, and once the queues drain the five databases must agree to the cent.
    /// </summary>
    [Fact]
    public async Task Money_reconciles_across_five_databases_after_a_broker_restart_and_two_killed_services()
    {
        const int count = 30;
        TimeSpan patience = TimeSpan.FromMinutes(3);

        World world = await World.CreateAsync(stack.Gateway, allotted: 1_000_000m);
        QuietState before = await stack.Quiescence.LookAsync(CancellationToken.None);
        var clock = Stopwatch.StartNew();

        Task<Purchase[]> buying = Task.WhenAll(Enumerable.Range(0, count).Select(async i =>
        {
            // Every total differs: equal totals from one supplier within a week are held as suspected duplicates.
            await Task.Delay(TimeSpan.FromMilliseconds(500 * i));
            return await Purchase.ToPayableAsync(stack.Gateway, world, quantity: 1 + (i % 3), unitPrice: 1_000m + i, patience);
        }));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await Containers.RestartAsync("rabbitmq");
            output.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} broker restarted");

            await Containers.KillAsync("budgets");
            output.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} budgets killed");
            await Task.Delay(TimeSpan.FromSeconds(3));
            await Containers.StartAsync("budgets");

            await Containers.KillAsync("payables");
            output.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} payables killed");
            await Task.Delay(TimeSpan.FromSeconds(3));
            await Containers.StartAsync("payables");
            output.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} all containers started again");
        }
        finally
        {
            // Whatever failed above, leave the stack running for the next test.
            await Containers.StartAsync("budgets");
            await Containers.StartAsync("payables");
        }

        Purchase[] purchases = await buying;
        output.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} all {count} invoices payable");

        JsonElement run = await PaymentRun.DraftAndReleaseAsync(stack.Gateway);
        JsonElement ours = run.GetProperty("creditors").EnumerateArray().Single(creditor => creditor.Id("supplierId") == world.Supplier);
        ours.Text("status").ShouldBe("Paid");
        ours.GetProperty("itemCount").GetInt32().ShouldBe(count);
        ours.Number("total").ShouldBe(purchases.Sum(static p => p.Amount));

        IReadOnlyList<Discrepancy> discrepancies = await stack.ReconcileAsync(patience);
        output.WriteLine($"{clock.Elapsed:mm\\:ss\\.f} quiet, {Reconciliation.Invariants.Length} invariants checked across five databases");

        JsonElement budget = await stack.Gateway.GetAsync(TestUsers.Bob, $"budgets/{world.Budget}");
        budget.Number("reserved").ShouldBe(0m);
        budget.Number("committed").ShouldBe(0m);
        budget.Number("actual").ShouldBe(purchases.Sum(static p => p.Amount));

        QuietState after = await stack.Quiescence.LookAsync(CancellationToken.None);
        after.ErrorQueues
            .Where(queue => queue.Value > before.ErrorQueues.GetValueOrDefault(queue.Key))
            .ShouldBeEmpty("a message was parked on an error queue during the run");

        discrepancies.ShouldBeEmpty(string.Join(Environment.NewLine, discrepancies));
    }
}
