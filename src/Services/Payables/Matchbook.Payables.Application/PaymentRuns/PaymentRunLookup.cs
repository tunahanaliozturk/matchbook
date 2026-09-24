using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.PaymentRuns;

internal static class PaymentRunLookup
{
    public static async Task<PaymentRun> SingleOrNotFoundAsync(
        this IQueryable<PaymentRun> runs,
        Guid paymentRunId,
        CancellationToken cancellationToken) =>
        await runs.SingleOrDefaultAsync(run => run.Id == paymentRunId, cancellationToken)
        ?? throw new BusinessRuleException("payment_run.not_found", "There is no such payment run.", ViolationKind.NotFound);
}
