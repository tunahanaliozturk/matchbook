using MassTransit;
using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Requisitions;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Infrastructure;

// Adapters from the bus to the handlers, and nothing more. Each runs inside the transaction the outbox opened, so
// the handler's balance UPDATE, its rows, the inbox record and the events it publishes commit together. Queue
// names come from the type names: RequisitionSubmittedConsumer listens on budgets-requisition-submitted.

public sealed class RequisitionSubmittedConsumer(IIntegrationEventHandler<RequisitionSubmitted> handler)
    : IConsumer<RequisitionSubmitted>
{
    public Task Consume(ConsumeContext<RequisitionSubmitted> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class RequisitionRejectedConsumer(IIntegrationEventHandler<RequisitionRejected> handler)
    : IConsumer<RequisitionRejected>
{
    public Task Consume(ConsumeContext<RequisitionRejected> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class RequisitionCancelledConsumer(IIntegrationEventHandler<RequisitionCancelled> handler)
    : IConsumer<RequisitionCancelled>
{
    public Task Consume(ConsumeContext<RequisitionCancelled> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PurchaseOrderCommitmentRequestedConsumer(
    IIntegrationEventHandler<PurchaseOrderCommitmentRequested> handler) : IConsumer<PurchaseOrderCommitmentRequested>
{
    public Task Consume(ConsumeContext<PurchaseOrderCommitmentRequested> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class InvoiceMatchedConsumer(IIntegrationEventHandler<InvoiceMatched> handler) : IConsumer<InvoiceMatched>
{
    public Task Consume(ConsumeContext<InvoiceMatched> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class PurchaseOrderClosedConsumer(IIntegrationEventHandler<PurchaseOrderClosed> handler)
    : IConsumer<PurchaseOrderClosed>
{
    public Task Consume(ConsumeContext<PurchaseOrderClosed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
