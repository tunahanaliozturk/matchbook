using MassTransit;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Suppliers;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Infrastructure.Messaging;

// Adapters only: the rules, the idempotency and the tolerance of reordering all live in the handlers, which the
// outbox wraps in one transaction per message together with the inbox record and anything they publish.

public sealed class PurchaseOrderIssuedConsumer(IIntegrationEventHandler<PurchaseOrderIssued> handler) : IConsumer<PurchaseOrderIssued>
{
    public Task Consume(ConsumeContext<PurchaseOrderIssued> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PurchaseOrderClosedConsumer(IIntegrationEventHandler<PurchaseOrderClosed> handler) : IConsumer<PurchaseOrderClosed>
{
    public Task Consume(ConsumeContext<PurchaseOrderClosed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class GoodsReceivedConsumer(IIntegrationEventHandler<GoodsReceived> handler) : IConsumer<GoodsReceived>
{
    public Task Consume(ConsumeContext<GoodsReceived> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class SupplierChangedConsumer(IIntegrationEventHandler<SupplierChanged> handler) : IConsumer<SupplierChanged>
{
    public Task Consume(ConsumeContext<SupplierChanged> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
