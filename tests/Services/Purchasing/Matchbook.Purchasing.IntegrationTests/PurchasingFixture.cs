using System.Net;
using MassTransit.EntityFrameworkCoreIntegration;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Requisitions;
using Matchbook.Contracts.Suppliers;
using Matchbook.Purchasing.Application.Features.PurchaseOrders;
using Matchbook.Purchasing.Infrastructure.Persistence;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestInfrastructure = Matchbook.Testing.Infrastructure;

[assembly: AssemblyFixture(typeof(Matchbook.Testing.Infrastructure))]

namespace Matchbook.Purchasing.IntegrationTests;

/// <summary>
/// One Purchasing host per test class, on its own database and virtual host, with a probe standing in for
/// Requisitions, Budgets, Suppliers and Payables: it publishes what they would and records what Purchasing sends.
/// Tests in a class share the host, so each works on its own requisition, supplier and order ids.
/// </summary>
public sealed class PurchasingFixture(TestInfrastructure infrastructure) : IAsyncLifetime
{
    public ServiceHost<Program> Host { get; private set; } = null!;

    public EventProbe Probe { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = await ServiceHost.StartAsync<Program>(infrastructure, "purchasing");
        Probe = await EventProbe.StartAsync(Host.Broker, static listen => listen
            .For<PurchaseOrderCommitmentRequested>()
            .For<PurchaseOrderIssued>()
            .For<GoodsReceived>()
            .For<PurchaseOrderClosed>());
    }

    public async ValueTask DisposeAsync()
    {
        await Probe.DisposeAsync();
        await Host.DisposeAsync();
    }

    public HttpClient ClientFor(Actor actor) => Host.ClientFor(actor);

    /// <summary>
    /// Publishes and waits until Purchasing's consumer has committed it, for messages that are meant to change
    /// nothing visible: the inbox row is the only evidence they were handled at all.
    /// </summary>
    public async Task PublishAndWaitAsync<T>(T message, Guid? messageId = null)
        where T : class
    {
        Guid id = messageId ?? Guid.NewGuid();
        await Probe.PublishAsync(message, id);
        await Eventually.MatchesAsync(
            () => Host.InScopeAsync(services => services.GetRequiredService<PurchasingDbContext>()
                .Set<InboxState>()
                .AnyAsync(state => state.MessageId == id && state.Consumed != null)),
            static consumed => consumed);
    }

    /// <summary>A supplier Purchasing's local copy knows as active.</summary>
    public async Task<Guid> ActiveSupplierAsync()
    {
        Guid supplierId = Guid.CreateVersion7();
        await PublishAndWaitAsync(Messages.Supplier(supplierId, 1, SupplierStatus.Active));
        return supplierId;
    }

    /// <summary>Approves a requisition and waits for the draft Purchasing makes of it.</summary>
    public async Task<PurchaseOrderView> DraftAsync(Guid supplierId, params (decimal Quantity, decimal UnitPrice)[] lines)
    {
        RequisitionApproved approved = Messages.Approval(supplierId, lines);
        await Probe.PublishAsync(approved);

        HttpClient bruno = ClientFor(TestUsers.Bruno);
        HttpResponseMessage found = await Eventually.MatchesAsync(
            () => bruno.GetAsync(Routes.ForRequisition(approved.RequisitionId)),
            static response => response.StatusCode == HttpStatusCode.OK);

        return await found.ReadAsync<PurchaseOrderView>(HttpStatusCode.OK);
    }

    /// <summary>Bruno issues the order; returns once Budgets (the probe) has the request for this attempt.</summary>
    public async Task<PurchaseOrderCommitmentRequested> IssueAsync(Guid orderId, int attempt)
    {
        HttpResponseMessage response = await ClientFor(TestUsers.Bruno).PostAsync(Routes.Issue(orderId), null);
        (await response.ReadAsync<PurchaseOrderView>(HttpStatusCode.Accepted)).Status.ShouldBe(Domain.PurchaseOrderStatus.CommitmentPending);

        return await Probe.WaitForAsync<PurchaseOrderCommitmentRequested>(
            request => request.PurchaseOrderId == orderId && request.Attempt == attempt);
    }

    /// <summary>An order issued by Bruno on attempt 1, to an active supplier.</summary>
    public async Task<PurchaseOrderView> IssuedAsync(params (decimal Quantity, decimal UnitPrice)[] lines)
    {
        PurchaseOrderView draft = await DraftAsync(await ActiveSupplierAsync(), lines);
        PurchaseOrderCommitmentRequested request = await IssueAsync(draft.Id, 1);

        await Probe.PublishAsync(Messages.Committed(request));
        await Probe.WaitForAsync<PurchaseOrderIssued>(issued => issued.PurchaseOrderId == draft.Id);
        return await GetAsync(draft.Id);
    }

    public async Task<PurchaseOrderView> GetAsync(Guid orderId) =>
        await (await ClientFor(TestUsers.Bruno).GetAsync(Routes.Order(orderId))).ReadAsync<PurchaseOrderView>(HttpStatusCode.OK);

    /// <summary>
    /// How many <typeparamref name="T"/> matching <paramref name="predicate"/> arrived, after leaving time for one
    /// that should not exist to show up. The outbox polls every 100 ms, so a second is ample.
    /// </summary>
    public async Task<int> CountAfterSettlingAsync<T>(Func<T, bool> predicate)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        return Probe.Received<T>().Count(predicate);
    }
}
