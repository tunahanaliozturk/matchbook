using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Queries.GetPaymentRun;

public sealed class GetPaymentRunHandler(IPayablesDb db) : IQueryHandler<GetPaymentRunQuery, PaymentRunView>
{
    public async Task<PaymentRunView> HandleAsync(GetPaymentRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return PaymentRunView.From(await db.PaymentRuns.AsNoTracking().SingleOrNotFoundAsync(query.PaymentRunId, cancellationToken));
    }
}
