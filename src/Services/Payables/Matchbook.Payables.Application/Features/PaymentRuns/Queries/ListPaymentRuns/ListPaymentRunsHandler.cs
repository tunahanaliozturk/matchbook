using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Queries.ListPaymentRuns;

public sealed class ListPaymentRunsHandler(IPayablesDb db) : IQueryHandler<ListPaymentRunsQuery, Page<PaymentRunSummary>>
{
    public async Task<Page<PaymentRunSummary>> HandleAsync(ListPaymentRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        IQueryable<PaymentRun> runs = db.PaymentRuns.AsNoTracking();
        if (query.Status is { } status)
        {
            runs = runs.Where(run => run.Status == status);
        }

        // Runs are few, a handful a week, so the primary key is index enough for the status filter too.
        if (query.After is { } after)
        {
            runs = runs.Where(run => run.Id.CompareTo(after) < 0);
        }

        List<PaymentRunSummary> rows = await runs
            .OrderByDescending(run => run.Id)
            .Take(limit + 1)
            .Select(PaymentRunSummary.Projection)
            .ToListAsync(cancellationToken);

        return Paging.ToPage(rows, limit, run => run.Id);
    }
}
