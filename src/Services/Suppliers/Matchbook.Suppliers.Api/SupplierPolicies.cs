using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Authorization;

namespace Matchbook.Suppliers.Api;

/// <summary>
/// Coarse on purpose. The endpoints admit the supplier roles and the auditor; which of the two supplier roles may
/// do what, and who counts as a second person, is decided once in the domain. That keeps one source for each rule
/// and gives every refusal a supplier user meets a code (<c>supplier.role_required</c>, <c>supplier.self_approval</c>)
/// rather than a bare 403.
/// </summary>
internal static class SupplierPolicies
{
    public const string Read = "suppliers.read";
    public const string Write = "suppliers.write";

    public static AuthorizationBuilder AddSupplierPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddRolePolicy(Read, Roles.SupplierAdmin, Roles.SupplierApprover, Roles.Auditor)
            .AddRolePolicy(Write, Roles.SupplierAdmin, Roles.SupplierApprover);
}
