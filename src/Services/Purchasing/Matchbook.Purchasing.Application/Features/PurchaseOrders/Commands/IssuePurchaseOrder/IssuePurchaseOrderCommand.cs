using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.IssuePurchaseOrder;

public sealed record IssuePurchaseOrderCommand(Guid PurchaseOrderId, Actor Buyer) : ICommand<PurchaseOrderView>;
