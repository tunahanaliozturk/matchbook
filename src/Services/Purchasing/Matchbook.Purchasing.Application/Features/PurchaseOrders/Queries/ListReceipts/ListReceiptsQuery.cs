using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListReceipts;

public sealed record ListReceiptsQuery(Guid PurchaseOrderId) : IQuery<IReadOnlyList<GoodsReceiptView>>;
