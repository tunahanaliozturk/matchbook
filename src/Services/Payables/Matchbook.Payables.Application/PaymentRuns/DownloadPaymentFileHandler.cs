using System.Runtime.CompilerServices;
using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.PaymentRuns;

public sealed class DownloadPaymentFileHandler(
    IPayablesDb db,
    PayerAccount payer,
    IFieldProtector protector,
    PayablesMetrics metrics,
    TimeProvider time)
{
    public async Task<PaymentFile> HandleAsync(Guid paymentRunId, CancellationToken cancellationToken)
    {
        PaymentRun run = await db.PaymentRuns.AsNoTracking().SingleOrNotFoundAsync(paymentRunId, cancellationToken);
        if (run is not { Status: PaymentRunStatus.Released, ReleasedAt: { } releasedAt })
        {
            throw new BusinessRuleException(
                "payment_run.not_released",
                "A bank file exists only for a released payment run.",
                ViolationKind.Conflict);
        }

        var header = new PaymentFileHeader(
            run.Id.ToString("N"),
            releasedAt,
            run.ExecutionDate,
            run.PaidCount,
            run.PaidTotal,
            payer.Name,
            Iban.Parse(payer.Iban),
            Bic.Parse(payer.Bic));

        return new PaymentFile($"pain001-{run.Id:N}.xml", async (destination, token) =>
        {
            long started = time.GetTimestamp();
            await Pain001Writer.WriteAsync(destination, header, TransfersAsync(run, token), token);
            metrics.FileWritten(time.GetElapsedTime(started), run.PaidCount);
        });
    }

    private async IAsyncEnumerable<CreditTransfer> TransfersAsync(
        PaymentRun run,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Account numbers are decrypted here, once per supplier, as the file is being written and not before.
        Dictionary<Guid, (PaymentRunCreditor Creditor, Iban Iban)> creditors = run.Creditors
            .Where(creditor => creditor.Status == CreditorStatus.Paid)
            .ToDictionary(
                creditor => creditor.SupplierId,
                creditor => (creditor, Iban.Parse(protector.Unprotect(creditor.ProtectedIban))));

        IAsyncEnumerable<PaymentRunItem> paid = db.PaymentRunItems
            .AsNoTracking()
            .Where(item => item.PaymentRunId == run.Id && item.Status == PaymentRunItemStatus.Paid)
            .OrderBy(item => item.SupplierId)
            .ThenBy(item => item.InvoiceId)
            .AsAsyncEnumerable();

        await foreach (PaymentRunItem item in paid.WithCancellation(cancellationToken))
        {
            (PaymentRunCreditor creditor, Iban iban) = creditors[item.SupplierId];
            yield return new CreditTransfer(
                item.InvoiceId.ToString("N"),
                item.Amount,
                creditor.AccountHolder,
                iban,
                creditor.Bic,
                item.SupplierInvoiceNumber);
        }
    }
}
