using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application.PurchaseOrders;

public sealed class GetReceiptHandler(IPurchasingDb db)
{
    public async Task<GoodsReceiptView> HandleAsync(Guid purchaseOrderId, Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await db.GoodsReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.Id == receiptId && receipt.PurchaseOrderId == purchaseOrderId, cancellationToken)
            ?? throw new BusinessRuleException(
                "goods_receipt.not_found",
                $"Purchase order {purchaseOrderId} has no receipt {receiptId}.",
                ViolationKind.NotFound);

        return GoodsReceiptView.From(receipt);
    }
}
