using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Matchbook.Payables.Application.Invoices;

/// <param name="Id">
/// Optional, chosen by the client so a retry after a lost response returns the invoice instead of capturing a second
/// one. A version 7 GUID keeps the invoice list in capture order.
/// </param>
public sealed record CaptureInvoice(
    Guid? Id,
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
public sealed class CaptureInvoiceHandler(IPayablesDb db, InvoiceMatcher matcher, PayablesMetrics metrics, TimeProvider time)
{
    public async Task<InvoiceView> HandleAsync(CaptureInvoice command, Actor clerk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        if (command.Id is { } requestedId
            && await db.Invoices.AsNoTracking().SingleOrDefaultAsync(existing => existing.Id == requestedId, cancellationToken) is { } earlier)
        {
            return IsSameCapture(earlier, command)
                ? InvoiceView.From(earlier)
                : throw new BusinessRuleException(
                    "request.id_reused",
                    "An invoice with this id was already captured with different content.",
                    ViolationKind.Conflict);
        }

        DateTimeOffset now = time.GetUtcNow();
        Invoice invoice = Invoice.Capture(
            command.Id ?? Guid.CreateVersion7(now),
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

        await using IDbContextTransaction transaction = await db.BeginTransactionAsync(cancellationToken);

        // The invoice row goes in first, on its own. Two captures of one number then meet at the unique index before
        // either touches the order, so the loser is told invoice.duplicate rather than a concurrency conflict on the
        // order row that a retry would turn into the same answer anyway.
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

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
        await transaction.CommitAsync(cancellationToken);

        metrics.Captured();
        return InvoiceView.From(invoice);
    }

    private static bool IsSameCapture(Invoice earlier, CaptureInvoice command) =>
        earlier.SupplierId == command.SupplierId
        && string.Equals(earlier.Number, command.SupplierInvoiceNumber?.Trim(), StringComparison.Ordinal)
        && earlier.InvoiceDate == command.InvoiceDate
        && earlier.PurchaseOrderId == command.PurchaseOrderId
        && earlier.Total == command.Total
        && earlier.Lines.SequenceEqual(command.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line => new InvoiceLine(line.LineNumber, line.Quantity, line.UnitPrice)));
}
