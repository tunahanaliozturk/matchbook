using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Invoices;

public sealed record CaptureInvoice(
    Guid SupplierId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    Guid PurchaseOrderId,
    IReadOnlyList<CaptureInvoiceLine> Lines,
    decimal Total);

public sealed record CaptureInvoiceLine(int LineNumber, decimal Quantity, decimal UnitPrice);

/// <summary>
/// Captures a supplier invoice: refuses a number the supplier already used, holds one that looks like a duplicate,
/// and otherwise runs the three-way match straight away.
/// </summary>
public sealed class CaptureInvoiceHandler(IPayablesDb db, InvoiceMatcher matcher, TimeProvider time)
{
    public async Task<InvoiceView> HandleAsync(CaptureInvoice command, Actor clerk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        DateTimeOffset now = time.GetUtcNow();
        Invoice invoice = Invoice.Capture(
            Guid.CreateVersion7(now),
            command.SupplierId,
            command.SupplierInvoiceNumber,
            command.InvoiceDate,
            command.PurchaseOrderId,
            [.. command.Lines.Select(line => new InvoiceLine(line.LineNumber, line.Quantity, line.UnitPrice))],
            command.Total,
            clerk,
            now);

        // The unique index on (supplier, normalised number) settles a race between two captures; this check gives the
        // common case a clear answer without relying on it.
        bool numberTaken = await db.Invoices.AnyAsync(
            other => other.SupplierId == invoice.SupplierId
                && other.NormalisedNumber == invoice.NormalisedNumber
                && other.Status != InvoiceStatus.Rejected,
            cancellationToken);
        if (numberTaken)
        {
            throw new BusinessRuleException(
                "invoice.duplicate",
                $"The supplier already has an invoice numbered {invoice.Number}.",
                ViolationKind.Conflict);
        }

        List<DuplicateCandidate> lookalikes = await db.Invoices
            .AsNoTracking()
            .Where(other => other.SupplierId == invoice.SupplierId && other.Total == invoice.Total)
            .Select(other => new DuplicateCandidate(
                other.Id,
                other.SupplierId,
                other.NormalisedNumber,
                other.InvoiceDate,
                other.Total,
                other.Status))
            .ToListAsync(cancellationToken);

        db.Invoices.Add(invoice);
        if (SuspectedDuplicate.FindFor(invoice, lookalikes) is { } original)
        {
            invoice.HoldAsSuspectedDuplicate(original.InvoiceId);
        }
        else
        {
            PurchaseOrder order = await matcher.ClaimOrderAsync(invoice.PurchaseOrderId, cancellationToken);
            await matcher.MatchAsync(order, invoice, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return InvoiceView.From(invoice);
    }
}
