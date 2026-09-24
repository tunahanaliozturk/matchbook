using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.PaymentRuns;

/// <summary>
/// The set-based side of releasing and cancelling a run: a run's items follow the decision the domain made for the
/// run or its creditors, and their invoices with them. One statement each, however many invoices the run holds.
/// </summary>
/// <remarks>
/// Every statement is guarded by the status it expects (an invoice is only paid if it is still scheduled, an item
/// only moves on while it is scheduled), so a statement that finds the world other than the domain assumed changes
/// fewer rows, and the caller counts them.
/// </remarks>
internal static class ScheduledInvoices
{
    /// <summary>
    /// Returns the run's scheduled invoices to payable, only those of <paramref name="supplierIds"/> when given, and
    /// moves their items to <paramref name="itemStatus"/>.
    /// </summary>
    public static async Task ReturnToPayableAsync(
        this IPayablesDb db,
        Guid paymentRunId,
        IReadOnlyCollection<Guid>? supplierIds,
        PaymentRunItemStatus itemStatus,
        CancellationToken cancellationToken)
    {
        IQueryable<PaymentRunItem> items = db.PaymentRunItems
            .Where(item => item.PaymentRunId == paymentRunId && item.Status == PaymentRunItemStatus.Scheduled);
        if (supplierIds is not null)
        {
            items = items.Where(item => supplierIds.Contains(item.SupplierId));
        }

        await db.Invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Scheduled && items.Any(item => item.InvoiceId == invoice.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(invoice => invoice.Status, InvoiceStatus.Payable), cancellationToken);
        await items.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, itemStatus), cancellationToken);
    }

    /// <summary>Marks the run's remaining scheduled invoices and items paid. Returns how many of each changed.</summary>
    public static async Task<(int Invoices, int Items)> PayAsync(
        this IPayablesDb db,
        Guid paymentRunId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IQueryable<PaymentRunItem> items = db.PaymentRunItems
            .Where(item => item.PaymentRunId == paymentRunId && item.Status == PaymentRunItemStatus.Scheduled);

        int invoices = await db.Invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Scheduled && items.Any(item => item.InvoiceId == invoice.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(invoice => invoice.Status, InvoiceStatus.Paid)
                    .SetProperty(invoice => invoice.PaidAt, now),
                cancellationToken);
        int paidItems = await items.ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.Status, PaymentRunItemStatus.Paid),
            cancellationToken);

        return (invoices, paidItems);
    }
}
