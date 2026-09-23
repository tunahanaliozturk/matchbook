using Matchbook.SharedKernel;

namespace Matchbook.Testing;

/// <summary>
/// The people in the Keycloak realm (<c>deploy/keycloak</c>), with the same ids and roles, so a test reads like the
/// scenario it describes: rita requests, mark approves, rosa receives.
/// </summary>
public static class TestUsers
{
    public static readonly Actor Rita = User(1, "rita", Roles.Requester);
    public static readonly Actor Mark = User(2, "mark", Roles.Approver);
    public static readonly Actor Maya = User(3, "maya", Roles.Approver);
    public static readonly Actor Fiona = User(4, "fiona", Roles.FinanceApprover);
    public static readonly Actor Carl = User(5, "carl", Roles.Cfo);
    public static readonly Actor Bruno = User(6, "bruno", Roles.Buyer);
    public static readonly Actor Rosa = User(7, "rosa", Roles.Receiver);
    public static readonly Actor Alice = User(8, "alice", Roles.ApClerk);
    public static readonly Actor Aaron = User(9, "aaron", Roles.ApApprover);
    public static readonly Actor Tess = User(10, "tess", Roles.Treasurer);
    public static readonly Actor Trevor = User(11, "trevor", Roles.Treasurer);
    public static readonly Actor Sam = User(12, "sam", Roles.SupplierAdmin);
    public static readonly Actor Sofia = User(13, "sofia", Roles.SupplierApprover);
    public static readonly Actor Bob = User(14, "bob", Roles.BudgetAdmin);
    public static readonly Actor Audrey = User(15, "audrey", Roles.Auditor);

    /// <summary>Someone the realm has never heard of, holding whatever roles a test needs.</summary>
    public static Actor Stranger(params string[] roles) =>
        new(Guid.CreateVersion7(), "stranger", new HashSet<string>(roles, StringComparer.Ordinal));

    private static Actor User(int number, string name, params string[] roles) =>
        new(Guid.Parse($"a0000000-0000-4000-8000-{number:D12}"), name, new HashSet<string>(roles, StringComparer.Ordinal));
}
