using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.PaymentRuns;

public sealed class GetPaymentRunHandler(IPayablesDb db)
{
    public async Task<PaymentRunView> HandleAsync(Guid paymentRunId, CancellationToken cancellationToken) =>
        PaymentRunView.From(await db.PaymentRuns.AsNoTracking().SingleOrNotFoundAsync(paymentRunId, cancellationToken));
}
