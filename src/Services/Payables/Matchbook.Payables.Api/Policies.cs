using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Authorization;

namespace Matchbook.Payables.Api;

/// <summary>
/// Who may call what. The policies admit the roles that may attempt an action; the domain then applies the finer
/// rules, such as an approver who captured the invoice, or the treasurer who drafted the run, and answers with a code
/// the caller can act on.
/// </summary>
internal static class Policies
{
    public const string ReadInvoices = "invoices.read";
    public const string CaptureInvoices = "invoices.capture";
    public const string ReviewInvoices = "invoices.review";
    public const string ReadPaymentRuns = "payment-runs.read";
    public const string ManagePaymentRuns = "payment-runs.manage";

    public static AuthorizationBuilder AddPayablesPolicies(this AuthorizationBuilder authorization) =>
        authorization
            .AddRolePolicy(ReadInvoices, Roles.ApClerk, Roles.ApApprover, Roles.Treasurer, Roles.Auditor)
            .AddRolePolicy(CaptureInvoices, Roles.ApClerk)

            // Clerks are let through so the domain can tell them why they may not approve, instead of a bare 403.
            .AddRolePolicy(ReviewInvoices, Roles.ApClerk, Roles.ApApprover)
            .AddRolePolicy(ReadPaymentRuns, Roles.Treasurer, Roles.Auditor)

            // The bank file carries account numbers in full, so it is here and not under the auditor's read policy.
            .AddRolePolicy(ManagePaymentRuns, Roles.Treasurer);
}
