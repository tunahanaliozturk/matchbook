using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Domain;

/// <summary>
/// A company Matchbook buys from and pays. Created and submitted by a supplier admin, activated by a supplier
/// approver who did not submit it, and paid only into a bank account a second person approved.
/// </summary>
public sealed class Supplier
{
    public const int ReasonMaxLength = 500;

    private readonly List<BankAccount> _bankAccounts = [];

    // The only constructor, so EF binds it too; the members it leaves out are set through private setters.
    private Supplier(
        Guid id,
        string legalName,
        TaxId taxId,
        CountryCode country,
        int paymentTermsDays,
        string contactEmail)
    {
        Id = id;
        LegalName = legalName;
        TaxId = taxId;
        Country = country;
        PaymentTermsDays = paymentTermsDays;
        ContactEmail = contactEmail;
    }

    public Guid Id { get; private set; }

    public string LegalName { get; private set; }

    public TaxId TaxId { get; private set; }

    public CountryCode Country { get; private set; }

    public int PaymentTermsDays { get; private set; }

    public string ContactEmail { get; private set; }

    public SupplierStatus Status { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? SubmittedBy { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? ActivatedBy { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public string? BlockReason { get; private set; }

    public Guid? BlockedBy { get; private set; }

    public DateTimeOffset? BlockedAt { get; private set; }

    /// <summary>
    /// The version of the supplier as other services see it: 0 until the first activation, then one more with
    /// every change they act on. The <c>SupplierChanged</c> snapshot carries it.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>How many bank accounts have been approved. The one approved last is in force.</summary>
    public int AccountVersion { get; private set; }

    /// <summary>Every account ever proposed, approved or rejected.</summary>
    public IReadOnlyCollection<BankAccount> BankAccounts => _bankAccounts;

    /// <summary>The account payments go to, or null before the first approval.</summary>
    public BankAccount? VerifiedAccount =>
        AccountVersion == 0 ? null : _bankAccounts.Single(account => account.AccountVersion == AccountVersion);

    public BankAccount? PendingAccount =>
        _bankAccounts.SingleOrDefault(account => account.Status == BankAccountStatus.Pending);

    /// <summary>
    /// The id comes from the caller so a client can retry a create it never heard back from without making a
    /// second supplier.
    /// </summary>
    public static Supplier Create(Actor actor, Guid id, SupplierDetails details, DateTimeOffset now)
    {
        RequireRole(actor, Roles.SupplierAdmin, "create a supplier");

        return new Supplier(
            id,
            details.LegalName,
            details.TaxId,
            details.Country,
            details.PaymentTermsDays,
            details.ContactEmail)
        {
            Status = SupplierStatus.Draft,
            CreatedBy = actor.Id,
            CreatedAt = now,
        };
    }

    public bool HasDetails(SupplierDetails details) =>
        LegalName == details.LegalName
        && TaxId == details.TaxId
        && Country == details.Country
        && PaymentTermsDays == details.PaymentTermsDays
        && ContactEmail == details.ContactEmail;

    /// <summary>
    /// One person may change the details in any state. Only the bank account and activation are under four
    /// eyes; the name and terms reach other services as a new version once the supplier has been active.
    /// </summary>
    public void ChangeDetails(Actor actor, SupplierDetails details)
    {
        RequireRole(actor, Roles.SupplierAdmin, "change a supplier");

        bool seenByOtherServices = LegalName != details.LegalName
            || Country != details.Country
            || PaymentTermsDays != details.PaymentTermsDays;

        LegalName = details.LegalName;
        TaxId = details.TaxId;
        Country = details.Country;
        PaymentTermsDays = details.PaymentTermsDays;
        ContactEmail = details.ContactEmail;

        if (seenByOtherServices)
        {
            RecordChange();
        }
    }

    public void Submit(Actor actor, DateTimeOffset now)
    {
        RequireRole(actor, Roles.SupplierAdmin, "submit a supplier for activation");
        RequireStatus(SupplierStatus.Draft, "submitted");

        Status = SupplierStatus.PendingActivation;
        SubmittedBy = actor.Id;
        SubmittedAt = now;
    }

    public void Activate(Actor actor, DateTimeOffset now)
    {
        RequireRole(actor, Roles.SupplierApprover, "activate a supplier");
        RequireStatus(SupplierStatus.PendingActivation, "activated");

        if (actor.Id == SubmittedBy)
        {
            throw SelfApproval("activate a supplier they submitted");
        }

        if (AccountVersion == 0)
        {
            throw new BusinessRuleException(
                "supplier.no_verified_account",
                "A supplier needs an approved bank account before it can be activated.",
                ViolationKind.Conflict);
        }

        Status = SupplierStatus.Active;
        ActivatedBy = actor.Id;
        ActivatedAt = now;
        RecordChange();
    }

    public void Block(Actor actor, string? reason, DateTimeOffset now)
    {
        RequireSupplierRole(actor, "block a supplier");
        RequireStatus(SupplierStatus.Active, "blocked");

        BlockReason = Reason(reason);
        BlockedBy = actor.Id;
        BlockedAt = now;
        Status = SupplierStatus.Blocked;
        RecordChange();
    }

    public void Unblock(Actor actor)
    {
        RequireRole(actor, Roles.SupplierApprover, "unblock a supplier");
        RequireStatus(SupplierStatus.Blocked, "unblocked");

        BlockReason = null;
        BlockedBy = null;
        BlockedAt = null;
        Status = SupplierStatus.Active;
        RecordChange();
    }

    /// <summary>
    /// Records a new account for a second person to approve. The verified account stays in force meanwhile, so
    /// a payment run drafted now still pays the account it was drafted against.
    /// </summary>
    public BankAccount ProposeBankAccount(
        Actor actor, Guid id, Iban iban, Bic bic, string? accountHolder, DateTimeOffset now)
    {
        RequireRole(actor, Roles.SupplierAdmin, "propose a bank account");

        if (PendingAccount is not null)
        {
            throw new BusinessRuleException(
                "supplier.bank_account_pending",
                "Another bank account is waiting for approval. Approve or reject it first.",
                ViolationKind.Conflict);
        }

        // Approving an identical account would raise the account version for nothing, and every payment run
        // drafted against the old version would drop this supplier's invoices at release.
        if (VerifiedAccount?.HasSameDetailsAs(iban, bic, accountHolder) == true)
        {
            throw new BusinessRuleException(
                "supplier.bank_account_unchanged",
                "This is the bank account already in force.",
                ViolationKind.Conflict);
        }

        var account = BankAccount.Propose(id, Id, iban, bic, accountHolder, actor, now);
        _bankAccounts.Add(account);
        return account;
    }

    public void ApproveBankAccount(Actor actor, Guid bankAccountId, DateTimeOffset now)
    {
        RequireRole(actor, Roles.SupplierApprover, "approve a bank account");
        BankAccount account = PendingAccountWithId(bankAccountId);

        if (actor.Id == account.ProposedBy)
        {
            throw SelfApproval("approve a bank account they proposed");
        }

        AccountVersion++;
        account.Approve(actor, AccountVersion, now);
        RecordChange();
    }

    /// <summary>
    /// Either role may reject, the proposer included: refusing a change leaves the verified account in force,
    /// so it needs no second person.
    /// </summary>
    public void RejectBankAccount(Actor actor, Guid bankAccountId, string? reason, DateTimeOffset now)
    {
        RequireSupplierRole(actor, "reject a bank account");
        BankAccount account = PendingAccountWithId(bankAccountId);

        account.Reject(actor, Reason(reason), now);
    }

    // Nothing is published before the first activation, so nothing counts until then either; the first
    // snapshot another service sees is version 1.
    private void RecordChange()
    {
        if (ActivatedAt is not null)
        {
            Version++;
        }
    }

    private BankAccount PendingAccountWithId(Guid bankAccountId)
    {
        BankAccount account = _bankAccounts.Find(candidate => candidate.Id == bankAccountId)
            ?? throw new BusinessRuleException(
                "supplier.bank_account_not_found",
                "The supplier has no such bank account.",
                ViolationKind.NotFound);

        return account.Status == BankAccountStatus.Pending
            ? account
            : throw new BusinessRuleException(
                "supplier.bank_account_not_pending",
                $"The bank account was already {account.Status.ToString().ToLowerInvariant()}.",
                ViolationKind.Conflict);
    }

    private void RequireStatus(SupplierStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new BusinessRuleException(
                "supplier.invalid_transition",
                $"A supplier in {Status} cannot be {action}; it has to be {expected}.",
                ViolationKind.Conflict);
        }
    }

    private static void RequireRole(Actor actor, string role, string action)
    {
        if (!actor.IsIn(role))
        {
            throw new BusinessRuleException(
                "supplier.role_required", $"Only a {role} may {action}.", ViolationKind.Forbidden);
        }
    }

    private static void RequireSupplierRole(Actor actor, string action)
    {
        if (!actor.IsIn(Roles.SupplierAdmin) && !actor.IsIn(Roles.SupplierApprover))
        {
            throw new BusinessRuleException(
                "supplier.role_required",
                $"Only a {Roles.SupplierAdmin} or a {Roles.SupplierApprover} may {action}.",
                ViolationKind.Forbidden);
        }
    }

    private static BusinessRuleException SelfApproval(string action) =>
        new("supplier.self_approval", $"A second person has to {action}.", ViolationKind.Forbidden);

    private static string Reason(string? input)
    {
        string reason = input?.Trim() ?? string.Empty;

        return reason.Length is > 0 and <= ReasonMaxLength
            ? reason
            : throw new BusinessRuleException(
                "supplier.reason_invalid",
                $"A reason is required and has at most {ReasonMaxLength} characters.",
                ViolationKind.Invalid);
    }
}
