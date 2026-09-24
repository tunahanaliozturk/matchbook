using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Commands.RecordReceipt;

/// <param name="ReceiptId">
/// The client's id for the receipt, so a retry after a lost response is recognised. Null lets the server pick one.
/// </param>
public sealed record RecordReceiptCommand(Guid PurchaseOrderId, Guid? ReceiptId, IReadOnlyList<LineQuantity> Lines, Actor Receiver)
    : ICommand<GoodsReceiptView>;
