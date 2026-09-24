using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Authorization;

namespace Matchbook.Requisitions.Api;

/// <summary>
/// Who may call what, by realm role. These only let a caller through the door: whether this approver may take
/// this step, or this requester may change this requisition, is the domain's decision.
/// </summary>
internal static class Policies
{
    public const string Request = "requisitions.request";
    public const string Decide = "requisitions.decide";
    public const string Read = "requisitions.read";

    public static AuthorizationBuilder AddRequisitionPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddRolePolicy(Request, Roles.Requester)
            .AddRolePolicy(Decide, Roles.Approver, Roles.FinanceApprover, Roles.Cfo)

            // The auditor reads everything and is in no policy that changes anything.
            .AddRolePolicy(Read, Roles.Requester, Roles.Approver, Roles.FinanceApprover, Roles.Cfo, Roles.Auditor);
}
