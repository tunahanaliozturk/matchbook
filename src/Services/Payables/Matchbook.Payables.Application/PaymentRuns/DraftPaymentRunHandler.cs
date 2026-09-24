using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Matchbook.Payables.Application.PaymentRuns;

/// <param name="Id">Optional, chosen by the client so a retry returns the run instead of drafting a second one.</param>
public sealed record DraftPaymentRun(Guid? Id, DateOnly ExecutionDate);

/// <summary>A treasurer drafts a payment run for an execution date: every payable invoice due by then that can be paid.</summary>
public sealed class DraftPaymentRunHandler(IPayablesDb db, TimeProvider time)
{
    public async Task<PaymentRunView> HandleAsync(DraftPaymentRun command, Actor treasurer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id is { } requestedId
            && await db.PaymentRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Id == requestedId, cancellationToken) is { } earlier)
        {
            return earlier.ExecutionDate == command.ExecutionDate
                ? PaymentRunView.From(earlier)
                : throw new BusinessRuleException(
                    "request.id_reused",
                    "A payment run with this id was already drafted for another date.",
                    ViolationKind.Conflict);
        }

        DateTimeOffset now = time.GetUtcNow();
        DateOnly executionDate = command.ExecutionDate;

        // The query narrows the field cheaply; the domain applies the rule.
        List<PaymentCandidate> candidates = await db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.Payable
                && (invoice.DueDate == null || invoice.DueDate <= executionDate))
            .Select(invoice => new PaymentCandidate(
                invoice.Id,
                invoice.PurchaseOrderId,
                invoice.SupplierId,
                invoice.Number,
                invoice.Total,
                invoice.Status,
                invoice.InvoiceDate,
                invoice.DueDate))
            .ToListAsync(cancellationToken);

        Guid[] supplierIds = [.. candidates.Select(candidate => candidate.SupplierId).Distinct()];
        Dictionary<Guid, Supplier> suppliers = await db.Suppliers
            .AsNoTracking()
            .Where(supplier => supplierIds.Contains(supplier.Id))
            .ToDictionaryAsync(supplier => supplier.Id, cancellationToken);

        PaymentRunDraft draft = PaymentRun.Draft(command.Id ?? Guid.CreateVersion7(now), executionDate, treasurer, candidates, suppliers, now);
        Guid runId = draft.Run.Id;

        await using IDbContextTransaction transaction = await db.BeginTransactionAsync(cancellationToken);

        db.PaymentRuns.Add(draft.Run);
        db.PaymentRunItems.AddRange(draft.Items);

        // Two drafts at the same moment read the same candidates. The index that allows one active run per invoice
        // makes the second insert wait for the first to commit and then fail, which surfaces as
        // payment_run.invoice_taken, and the transaction takes the whole of the losing draft with it.
        await db.SaveChangesAsync(cancellationToken);

        int scheduled = await db.Invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Payable
                && db.PaymentRunItems.Any(item => item.PaymentRunId == runId && item.InvoiceId == invoice.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(invoice => invoice.Status, InvoiceStatus.Scheduled), cancellationToken);
        if (scheduled != draft.Items.Count)
        {
            throw new BusinessRuleException(
                "payment_run.invoice_taken",
                "Some of these invoices were taken by another payment run while this one was drafted. Draft again.",
                ViolationKind.Conflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return PaymentRunView.From(draft.Run);
    }
}
