using System.Net.Http.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Application.Budgets;
using Matchbook.Budgets.Application.CostCentres;
using Matchbook.Budgets.Domain;
using Matchbook.Budgets.Infrastructure;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Requisitions;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Matchbook.Testing.Infrastructure))]

namespace Matchbook.Budgets.IntegrationTests;

/// <summary>
/// One Budgets host per test class, on its own database and virtual host, with a probe that stands in for the
/// other services. Tests in a class share it, so each one works on its own cost centre and budget.
/// </summary>
public sealed class BudgetsFixture(Matchbook.Testing.Infrastructure infrastructure) : IAsyncLifetime
{
    public const int FiscalYear = 2026;

    public ServiceHost<Program> Host { get; private set; } = null!;

    public EventProbe Probe { get; private set; } = null!;

    public HttpClient Bob { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = await ServiceHost.StartAsync<Program>(infrastructure, "budgets");
        Probe = await EventProbe.StartAsync(Host.Broker, static listen => listen
            .For<FundsReserved>()
            .For<FundsReservationRejected>()
            .For<FundsCommitted>()
            .For<FundsCommitmentRejected>()
            .For<CostCentreChanged>());
        Bob = Host.ClientFor(TestUsers.Bob);
    }

    public async ValueTask DisposeAsync()
    {
        Bob.Dispose();
        await Probe.DisposeAsync();
        await Host.DisposeAsync();
    }

    /// <summary>A cost centre code no other test uses: the shared database outlives each test.</summary>
    public static string NewCode() => $"TST-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";

    public async Task<CostCentreView> CreateCostCentreAsync(string? code = null)
    {
        HttpResponseMessage response = await Bob.PostAsJsonAsync(
            new Uri("/cost-centres", UriKind.Relative),
            new { code = code ?? NewCode(), name = "Test cost centre", managerId = TestUsers.Mark.Id });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CostCentreView>())!;
    }

    public async Task<BudgetView> OpenBudgetAsync(decimal allotted)
    {
        CostCentreView costCentre = await CreateCostCentreAsync();
        HttpResponseMessage response = await Bob.PostAsJsonAsync(
            new Uri("/budgets", UriKind.Relative),
            new { costCentreCode = costCentre.Code, fiscalYear = FiscalYear, allotted });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetView>())!;
    }

    public async Task<BudgetView> BudgetAsync(Guid budgetId) =>
        (await Bob.GetFromJsonAsync<BudgetView>(new Uri($"/budgets/{budgetId}", UriKind.Relative)))!;

    public Task<T> InDatabaseAsync<T>(Func<BudgetsDbContext, Task<T>> query) =>
        Host.InScopeAsync(services => query(services.GetRequiredService<BudgetsDbContext>()));

    public Task<LedgerEntry[]> LedgerAsync(Guid budgetId) =>
        InDatabaseAsync(db => db.Ledger.AsNoTracking().Where(e => e.BudgetId == budgetId).OrderBy(e => e.Sequence).ToArrayAsync());

    /// <summary>The first reconciliation check: a budget's figures are the sum of its ledger.</summary>
    public async Task FiguresShouldEqualTheLedgerAsync(Guid budgetId)
    {
        BudgetView budget = await BudgetAsync(budgetId);
        LedgerEntry[] ledger = await LedgerAsync(budgetId);

        budget.Allotted.ShouldBe(ledger.Sum(e => e.Allotted));
        budget.Reserved.ShouldBe(ledger.Sum(e => e.Reserved));
        budget.Committed.ShouldBe(ledger.Sum(e => e.Committed));
        budget.Actual.ShouldBe(ledger.Sum(e => e.Actual));
    }

    /// <summary>
    /// Waits until a consumer has processed the message with this id, which is how a test knows that a message
    /// that should change nothing has actually been handled, rather than guessing a delay.
    /// </summary>
    public Task WaitUntilConsumedAsync(Guid messageId) =>
        Eventually.MatchesAsync(
            () => InDatabaseAsync(db => db.Set<InboxState>().AnyAsync(s => s.MessageId == messageId && s.Consumed != null)),
            static consumed => consumed);

    public async Task<Guid> ReserveAsync(BudgetView budget, decimal amount)
    {
        Guid requisitionId = Guid.CreateVersion7();
        await Probe.PublishAsync(Submitted(budget, requisitionId, amount));
        await Probe.WaitForAsync<FundsReserved>(reserved => reserved.RequisitionId == requisitionId);
        return requisitionId;
    }

    public async Task<Guid> CommitAsync(BudgetView budget, Guid requisitionId, decimal amount)
    {
        Guid orderId = Guid.CreateVersion7();
        await Probe.PublishAsync(CommitmentRequested(budget, orderId, requisitionId, attempt: 1, amount));
        await Probe.WaitForAsync<FundsCommitted>(committed => committed.PurchaseOrderId == orderId);
        return orderId;
    }

    /// <summary>Reserves, commits and invoices <paramref name="amount"/>, leaving it as actual spend.</summary>
    public async Task<Guid> SpendAsync(BudgetView budget, decimal amount)
    {
        Guid orderId = await CommitAsync(budget, await ReserveAsync(budget, amount), amount);
        Guid invoiceId = Guid.CreateVersion7();
        await Probe.PublishAsync(Invoiced(invoiceId, orderId, amount));
        await Eventually.MatchesAsync(
            () => InDatabaseAsync(db => db.Ledger.AnyAsync(e => e.DocumentId == invoiceId)),
            static recorded => recorded);
        return orderId;
    }

    public static InvoiceMatched Invoiced(Guid invoiceId, Guid orderId, decimal amount) =>
        new(invoiceId, orderId, Guid.CreateVersion7(), $"INV-{invoiceId.ToString("N")[..6]}", [], amount, DateTimeOffset.UtcNow);

    public static RequisitionSubmitted Submitted(BudgetView budget, Guid requisitionId, decimal amount) =>
        new(
            requisitionId,
            $"REQ-{FiscalYear}-{requisitionId.ToString("N")[..6]}",
            TestUsers.Rita.Id,
            budget.CostCentreCode,
            budget.FiscalYear,
            Guid.CreateVersion7(),
            amount,
            DateTimeOffset.UtcNow);

    public static PurchaseOrderCommitmentRequested CommitmentRequested(
        BudgetView budget, Guid orderId, Guid requisitionId, int attempt, decimal amount) =>
        new(orderId, requisitionId, attempt, budget.CostCentreCode, budget.FiscalYear, amount, DateTimeOffset.UtcNow);
}
