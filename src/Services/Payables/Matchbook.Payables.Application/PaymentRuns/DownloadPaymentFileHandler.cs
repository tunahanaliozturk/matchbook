using System.Runtime.CompilerServices;
using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.PaymentRuns;

public sealed class DownloadPaymentFileHandler(IPayablesDb db, PayerAccount payer)
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
            payer.CompanyName,
            Iban.Parse(payer.Iban),
            Bic.Parse(payer.Bic));

        return new PaymentFile(
            $"pain001-{run.Id:N}.xml",
            (destination, token) => Pain001Writer.WriteAsync(destination, header, TransfersAsync(run, token), token));
    }

    private async IAsyncEnumerable<CreditTransfer> TransfersAsync(
        PaymentRun run,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Dictionary<Guid, PaymentRunCreditor> creditors = run.Creditors.ToDictionary(creditor => creditor.SupplierId);

        IAsyncEnumerable<PaymentRunItem> paid = db.PaymentRunItems
            .AsNoTracking()
            .Where(item => item.PaymentRunId == run.Id && item.Status == PaymentRunItemStatus.Paid)
            .OrderBy(item => item.SupplierId)
            .ThenBy(item => item.InvoiceId)
            .AsAsyncEnumerable();

        await foreach (PaymentRunItem item in paid.WithCancellation(cancellationToken))
        {
            PaymentRunCreditor creditor = creditors[item.SupplierId];
            yield return new CreditTransfer(
                item.InvoiceId.ToString("N"),
                item.Amount,
                creditor.AccountHolder,
                creditor.Iban,
                creditor.Bic,
                item.SupplierInvoiceNumber);
        }
    }
}
