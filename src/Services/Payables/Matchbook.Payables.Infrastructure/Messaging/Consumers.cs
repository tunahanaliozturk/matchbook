using MassTransit;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Suppliers;
using Matchbook.Payables.Application.Events;

namespace Matchbook.Payables.Infrastructure.Messaging;

// Adapters only: the rules, the idempotency and the tolerance of reordering all live in the handlers, which the
// outbox wraps in one transaction per message together with the inbox record and anything they publish.

public sealed class PurchaseOrderIssuedConsumer(PurchaseOrderIssuedHandler handler) : IConsumer<PurchaseOrderIssued>
{
    public Task Consume(ConsumeContext<PurchaseOrderIssued> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PurchaseOrderClosedConsumer(PurchaseOrderClosedHandler handler) : IConsumer<PurchaseOrderClosed>
{
    public Task Consume(ConsumeContext<PurchaseOrderClosed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class GoodsReceivedConsumer(GoodsReceivedHandler handler) : IConsumer<GoodsReceived>
{
    public Task Consume(ConsumeContext<GoodsReceived> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class SupplierChangedConsumer(SupplierChangedHandler handler) : IConsumer<SupplierChanged>
{
    public Task Consume(ConsumeContext<SupplierChanged> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
