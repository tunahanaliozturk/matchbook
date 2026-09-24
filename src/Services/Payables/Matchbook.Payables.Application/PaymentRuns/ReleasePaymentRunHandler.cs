using Matchbook.Contracts.Payables;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Matchbook.Payables.Application.PaymentRuns;

/// <summary>
/// A second treasurer releases a draft run: suppliers that can no longer be paid are dropped, everything else is
/// marked paid, and <see cref="InvoicePaid"/> goes out for each invoice paid, all in one transaction.
/// </summary>
public sealed class ReleasePaymentRunHandler(IPayablesDb db, IEventPublisher publisher, TimeProvider time)
{
    public async Task<PaymentRunView> HandleAsync(Guid paymentRunId, Actor treasurer, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await using IDbContextTransaction transaction = await db.BeginTransactionAsync(cancellationToken);

        PaymentRun run = await db.PaymentRuns.SingleOrNotFoundAsync(paymentRunId, cancellationToken);
        Guid[] supplierIds = [.. run.Creditors.Select(creditor => creditor.SupplierId)];
        Dictionary<Guid, Supplier> suppliers = await db.Suppliers
            .AsNoTracking()
            .Where(supplier => supplierIds.Contains(supplier.Id))
            .ToDictionaryAsync(supplier => supplier.Id, cancellationToken);

        run.Release(treasurer, suppliers, now);

        // The run is written first, under its row version. A second release of the same run is refused by the domain
        // if the first has committed, and fails here if it has not, holding the row until then: either way before
        // anything below runs for it.
        await db.SaveChangesAsync(cancellationToken);

        Guid[] dropped = [.. run.Creditors
            .Where(creditor => creditor.Status == CreditorStatus.Dropped)
            .Select(creditor => creditor.SupplierId)];
        if (dropped.Length > 0)
        {
            await db.ReturnToPayableAsync(run.Id, dropped, PaymentRunItemStatus.Dropped, cancellationToken);
        }

        (int invoices, int items) = await db.PayAsync(run.Id, now, cancellationToken);
        if (invoices != run.PaidCount || items != run.PaidCount)
        {
            throw new InvalidOperationException(
                $"Payment run {run.Id} should pay {run.PaidCount} invoices but found {invoices} invoices and {items} items to pay. Nothing was paid.");
        }

        var paid = await db.PaymentRunItems
            .AsNoTracking()
            .Where(item => item.PaymentRunId == run.Id && item.Status == PaymentRunItemStatus.Paid)
            .Select(item => new { item.InvoiceId, item.PurchaseOrderId, item.SupplierId, item.Amount })
            .ToListAsync(cancellationToken);
        foreach (var item in paid)
        {
            await publisher.PublishAsync(
                new InvoicePaid(item.InvoiceId, item.PurchaseOrderId, item.SupplierId, run.Id, item.Amount, run.ExecutionDate, now),
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentRunView.From(run);
    }
}
