using MassTransit;
using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Requisitions;
using Matchbook.Contracts.Suppliers;
using Matchbook.Purchasing.Application.IncomingEvents;

namespace Matchbook.Purchasing.Infrastructure.Messaging;

// Thin adapters from MassTransit to the Application handlers, which hold all the logic and the idempotency. The
// outbox around each consumer gives the handler its transaction, and holds what it publishes until it commits.

public sealed class RequisitionApprovedConsumer(RequisitionApprovedHandler handler) : IConsumer<RequisitionApproved>
{
    public Task Consume(ConsumeContext<RequisitionApproved> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class FundsCommittedConsumer(FundsCommittedHandler handler) : IConsumer<FundsCommitted>
{
    public Task Consume(ConsumeContext<FundsCommitted> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class FundsCommitmentRejectedConsumer(FundsCommitmentRejectedHandler handler) : IConsumer<FundsCommitmentRejected>
{
    public Task Consume(ConsumeContext<FundsCommitmentRejected> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class SupplierChangedConsumer(SupplierChangedHandler handler) : IConsumer<SupplierChanged>
{
    public Task Consume(ConsumeContext<SupplierChanged> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class InvoiceMatchedConsumer(InvoiceMatchedHandler handler) : IConsumer<InvoiceMatched>
{
    public Task Consume(ConsumeContext<InvoiceMatched> context) =>
        handler.HandleAsync(context.Message, context.CancellationToken);
}
