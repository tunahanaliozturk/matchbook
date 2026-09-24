using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.Budgets.Commands.ChangeAllotment;

/// <summary>
/// Raises (positive <paramref name="Change"/>) or lowers (negative) a budget's allotment. A change rather than a
/// new total, so two admins adjusting the same budget at once both get what they asked for.
/// </summary>
/// <param name="Id">Optional. Becomes the ledger entry's document id; repeating it returns the first result.</param>
/// <param name="Actor">The budget admin making the change, recorded on its ledger entry.</param>
public sealed record ChangeAllotmentCommand(Guid? Id, Guid BudgetId, decimal Change, Actor Actor)
    : ICommand<AllotmentChangeView>;
