using Matchbook.Contracts.Requisitions;
using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matchbook.Purchasing.Application.IncomingEvents;

/// <summary>Drafts the purchase order for an approved requisition, once, however often the message arrives.</summary>
/// <remarks>
/// The existence check handles the ordinary redelivery. Two copies consumed at the same moment both pass it, and
/// the unique index on the requisition id turns the second insert into a failure that is retried and then finds
/// the first order.
/// </remarks>
public sealed class RequisitionApprovedHandler(
    IPurchasingDb db, TimeProvider clock, PurchasingMetrics metrics, ILogger<RequisitionApprovedHandler> logger)
{
    public async Task HandleAsync(RequisitionApproved message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (await db.PurchaseOrders.AnyAsync(order => order.RequisitionId == message.RequisitionId, cancellationToken))
        {
            logger.AlreadyApplied(nameof(RequisitionApproved), message.RequisitionId);
            return;
        }

        long sequence = await db.NextPurchaseOrderSequenceAsync(cancellationToken);
        var requisition = new ApprovedRequisition(
            message.RequisitionId,
            message.SupplierId,
            message.CostCentreCode,
            message.FiscalYear,
            [
                .. message.Lines.Select(static line => new DraftLine(
                    line.LineNumber, line.Description, line.Quantity, line.UnitOfMeasure, line.UnitPrice)),
            ]);

        db.PurchaseOrders.Add(PurchaseOrder.Draft(requisition, sequence, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        metrics.OrderDrafted();
    }
}
