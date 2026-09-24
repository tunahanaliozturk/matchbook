using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.ShortClosePurchaseOrder;

public sealed record ShortClosePurchaseOrderCommand(Guid PurchaseOrderId, Actor Buyer) : ICommand<PurchaseOrderView>;
