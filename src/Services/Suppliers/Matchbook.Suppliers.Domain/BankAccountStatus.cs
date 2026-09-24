namespace Matchbook.Suppliers.Domain;

/// <summary>
/// Every bank account starts as a proposal. An approved one stays <see cref="Approved"/> after a later one
/// replaces it; the supplier's <c>AccountVersion</c> says which approval is in force.
/// </summary>
public enum BankAccountStatus
{
    Pending,
    Approved,
    Rejected,
}
