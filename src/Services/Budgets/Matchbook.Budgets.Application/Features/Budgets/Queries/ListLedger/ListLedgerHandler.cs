using System.Globalization;
using Matchbook.Budgets.Application.Common;
using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.ListLedger;

public sealed class ListLedgerHandler(IBudgetsDb db) : IQueryHandler<ListLedgerQuery, Page<LedgerEntryView>>
{
    public async Task<Page<LedgerEntryView>> HandleAsync(ListLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        int limit = Paging.Limit(query.Limit);
        long after = query.After ?? 0L;
        List<LedgerEntry> rows = await db.Ledger
            .AsNoTracking()
            .Where(e => e.BudgetId == query.BudgetId && e.Sequence > after)
            .OrderBy(e => e.Sequence)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        // Every budget's ledger starts with its opening entry, so an empty page is either the end of the ledger
        // or a budget that does not exist, and only then is it worth a second query.
        if (rows.Count == 0 && !await db.Budgets.AnyAsync(b => b.Id == query.BudgetId, cancellationToken))
        {
            throw NotFound.ForBudget(query.BudgetId);
        }

        return Paging.Of(
            rows.ConvertAll(LedgerEntryView.From),
            limit,
            view => view.Sequence.ToString(CultureInfo.InvariantCulture));
    }
}
