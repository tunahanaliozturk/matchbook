using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

public sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId, Actor Buyer) : ICommand<PurchaseOrderView>;
