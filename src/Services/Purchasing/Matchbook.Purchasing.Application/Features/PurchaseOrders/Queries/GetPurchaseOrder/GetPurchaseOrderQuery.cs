using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetPurchaseOrder;

public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrderView>;
