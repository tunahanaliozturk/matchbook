using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Authorization;

namespace Matchbook.Budgets.Api;

/// <summary>
/// Who may do what. Budget admins change budgets and cost centres. Budgets are readable by admins, the auditor, and
/// the approvers who decide on spending against them. Cost centres are reference data every signed-in user may
/// read, since a requester picks one; the fallback policy covers that.
/// </summary>
internal static class Policies
{
    public const string ReadBudgets = "budgets.read";
    public const string Administer = "budgets.administer";

    public static AuthorizationBuilder AddBudgetsPolicies(this AuthorizationBuilder authorization) =>
        authorization
            .AddRolePolicy(ReadBudgets, Roles.BudgetAdmin, Roles.Auditor, Roles.Approver, Roles.FinanceApprover, Roles.Cfo)
            .AddRolePolicy(Administer, Roles.BudgetAdmin);
}
