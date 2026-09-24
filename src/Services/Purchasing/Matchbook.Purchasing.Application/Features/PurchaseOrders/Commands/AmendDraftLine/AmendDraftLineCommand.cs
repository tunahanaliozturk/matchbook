using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.AmendDraftLine;

public sealed record AmendDraftLineCommand(Guid PurchaseOrderId, int LineNumber, decimal Quantity, decimal UnitPrice, Actor Buyer)
    : ICommand<PurchaseOrderView>;
