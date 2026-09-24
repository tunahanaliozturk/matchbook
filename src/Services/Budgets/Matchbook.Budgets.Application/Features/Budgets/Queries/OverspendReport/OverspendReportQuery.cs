using Matchbook.Budgets.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Queries.OverspendReport;

/// <summary>
/// The budgets of a fiscal year whose consumption has run past the allotment. Only an invoice larger than its
/// commitment can put a budget here, and the report is where that shows up.
/// </summary>
public sealed record OverspendReportQuery(int FiscalYear, string? After = null, int? Limit = null) : IQuery<Page<BudgetView>>;
