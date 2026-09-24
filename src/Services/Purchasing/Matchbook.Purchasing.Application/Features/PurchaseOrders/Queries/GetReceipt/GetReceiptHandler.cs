using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetReceipt;

public sealed class GetReceiptHandler(IPurchasingDb db) : IQueryHandler<GetReceiptQuery, GoodsReceiptView>
{
    public async Task<GoodsReceiptView> HandleAsync(GetReceiptQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var receipt = await db.GoodsReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                receipt => receipt.Id == query.ReceiptId && receipt.PurchaseOrderId == query.PurchaseOrderId, cancellationToken)
            ?? throw new BusinessRuleException(
                "goods_receipt.not_found",
                $"Purchase order {query.PurchaseOrderId} has no receipt {query.ReceiptId}.",
                ViolationKind.NotFound);

        return GoodsReceiptView.From(receipt);
    }
}
