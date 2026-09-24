using Matchbook.Budgets.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.ListLedger;

/// <summary>A budget's ledger in the order it was written. <paramref name="After"/> is the last sequence seen.</summary>
public sealed record ListLedgerQuery(Guid BudgetId, long? After = null, int? Limit = null) : IQuery<Page<LedgerEntryView>>;
