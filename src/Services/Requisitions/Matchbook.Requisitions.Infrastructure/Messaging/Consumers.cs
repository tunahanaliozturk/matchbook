using MassTransit;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Suppliers;
using Matchbook.Requisitions.Application.IncomingEvents;

namespace Matchbook.Requisitions.Infrastructure.Messaging;

// Adapters and nothing else: every decision about a message is made by its Application handler, which the
// unit tests and the integration tests reach without the bus. Each consumer gets its own queue, named for the
// service and the consumer (requisitions-funds-reserved), and the outbox and inbox around it come from
// AddMatchbookMessaging.

internal sealed class CostCentreChangedConsumer(CostCentreChangedHandler handler) : IConsumer<CostCentreChanged>
{
    public Task Consume(ConsumeContext<CostCentreChanged> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

internal sealed class SupplierChangedConsumer(SupplierChangedHandler handler) : IConsumer<SupplierChanged>
{
    public Task Consume(ConsumeContext<SupplierChanged> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

internal sealed class FundsReservedConsumer(FundsReservedHandler handler) : IConsumer<FundsReserved>
{
    public Task Consume(ConsumeContext<FundsReserved> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

internal sealed class FundsReservationRejectedConsumer(FundsReservationRejectedHandler handler) : IConsumer<FundsReservationRejected>
{
    public Task Consume(ConsumeContext<FundsReservationRejected> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

internal sealed class PurchaseOrderIssuedConsumer(PurchaseOrderIssuedHandler handler) : IConsumer<PurchaseOrderIssued>
{
    public Task Consume(ConsumeContext<PurchaseOrderIssued> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

internal sealed class PurchaseOrderClosedConsumer(PurchaseOrderClosedHandler handler) : IConsumer<PurchaseOrderClosed>
{
    public Task Consume(ConsumeContext<PurchaseOrderClosed> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
