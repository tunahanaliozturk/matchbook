namespace Matchbook.SharedKernel;

/// <summary>
/// Realm roles, as issued by the identity provider. Each maps to a job in a purchasing department, and the
/// separation-of-duties rules are written in terms of them.
/// </summary>
public static class Roles
{
    public const string Requester = "requester";
    public const string Approver = "approver";
    public const string FinanceApprover = "finance-approver";
    public const string Cfo = "cfo";
    public const string Buyer = "buyer";
    public const string Receiver = "receiver";
    public const string ApClerk = "ap-clerk";
    public const string ApApprover = "ap-approver";
    public const string Treasurer = "treasurer";
    public const string SupplierAdmin = "supplier-admin";
    public const string SupplierApprover = "supplier-approver";
    public const string BudgetAdmin = "budget-admin";
    public const string Auditor = "auditor";
}
