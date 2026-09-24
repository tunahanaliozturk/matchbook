using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Domain;

/// <summary>
/// One bank account a supplier admin proposed, and what became of it. The rows are the history: nothing is
/// updated after the decision and nothing is deleted.
/// </summary>
public sealed class BankAccount
{
    /// <summary>The creditor name in a pain.001 credit transfer is Max70Text.</summary>
    public const int AccountHolderMaxLength = 70;

    // The only constructor, so EF binds it too; the members it leaves out are set through private setters.
    private BankAccount(Guid id, Guid supplierId, Iban iban, Bic bic, string accountHolder)
    {
        Id = id;
        SupplierId = supplierId;
        Iban = iban;
        Bic = bic;
        AccountHolder = accountHolder;
    }

    public Guid Id { get; private set; }

    public Guid SupplierId { get; private set; }

    public Iban Iban { get; private set; }

    public Bic Bic { get; private set; }

    public string AccountHolder { get; private set; }

    public BankAccountStatus Status { get; private set; }

    public Guid ProposedBy { get; private set; }

    public DateTimeOffset ProposedAt { get; private set; }

    /// <summary>Who approved or rejected it.</summary>
    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    /// <summary>Set on approval: the supplier's account version this account became.</summary>
    public int? AccountVersion { get; private set; }

    /// <summary>
    /// The full IBAN is shown only to someone who could approve this proposal, because that person has to check
    /// it against the supplier's letter. Everyone else, the proposer included, sees it masked.
    /// </summary>
    public bool MayRevealIbanTo(Actor actor) =>
        Status == BankAccountStatus.Pending && actor.IsIn(Roles.SupplierApprover) && actor.Id != ProposedBy;

    internal static BankAccount Propose(
        Guid supplierId, Iban iban, Bic bic, string? accountHolder, Actor proposer, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), supplierId, iban, bic, Holder(accountHolder))
        {
            Status = BankAccountStatus.Pending,
            ProposedBy = proposer.Id,
            ProposedAt = now,
        };

    internal bool HasSameDetailsAs(Iban iban, Bic bic, string? accountHolder) =>
        Iban == iban && Bic == bic && AccountHolder == accountHolder?.Trim();

    internal void Approve(Actor approver, int accountVersion, DateTimeOffset now)
    {
        Status = BankAccountStatus.Approved;
        AccountVersion = accountVersion;
        DecidedBy = approver.Id;
        DecidedAt = now;
    }

    internal void Reject(Actor actor, string reason, DateTimeOffset now)
    {
        Status = BankAccountStatus.Rejected;
        RejectionReason = reason;
        DecidedBy = actor.Id;
        DecidedAt = now;
    }

    private static string Holder(string? input)
    {
        string holder = input?.Trim() ?? string.Empty;

        return holder.Length is > 0 and <= AccountHolderMaxLength
            ? holder
            : throw new BusinessRuleException(
                "supplier.account_holder_invalid",
                $"The account holder's name is required and has at most {AccountHolderMaxLength} characters.",
                ViolationKind.Invalid);
    }
}
