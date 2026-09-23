using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.UnitTests;

/// <summary>The people the tests act as. Ids follow the seeded realm where one exists.</summary>
internal static class People
{
    public static readonly Actor Admin = Person("a0000000-0000-4000-8000-000000000012", "sam", Roles.SupplierAdmin);

    public static readonly Actor Approver = Person("a0000000-0000-4000-8000-000000000013", "sofia", Roles.SupplierApprover);

    public static readonly Actor OtherAdmin = Person("b0000000-0000-4000-8000-000000000001", "sid", Roles.SupplierAdmin);

    public static readonly Actor OtherApprover = Person("b0000000-0000-4000-8000-000000000002", "sven", Roles.SupplierApprover);

    /// <summary>Holds both roles, which is exactly the case the four-eyes rules exist for.</summary>
    public static readonly Actor AdminAndApprover =
        Person("b0000000-0000-4000-8000-000000000003", "sasha", Roles.SupplierAdmin, Roles.SupplierApprover);

    public static readonly Actor Auditor = Person("a0000000-0000-4000-8000-000000000015", "audrey", Roles.Auditor);

    private static Actor Person(string id, string name, params string[] roles) =>
        new(Guid.Parse(id), name, new HashSet<string>(roles, StringComparer.Ordinal));
}
