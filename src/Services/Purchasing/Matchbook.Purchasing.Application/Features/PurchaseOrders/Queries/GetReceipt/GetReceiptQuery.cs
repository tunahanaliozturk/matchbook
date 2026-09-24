using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetReceipt;

public sealed record GetReceiptQuery(Guid PurchaseOrderId, Guid ReceiptId) : IQuery<GoodsReceiptView>;
