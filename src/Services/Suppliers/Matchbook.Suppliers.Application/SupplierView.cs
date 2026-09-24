using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application;

/// <summary>
/// A supplier as one particular person may see it. The same supplier reads differently to the approver
/// reviewing a bank account proposal, who gets that IBAN in full, and to everyone else.
/// </summary>
public sealed record SupplierView(
    Guid Id,
    string LegalName,
    string TaxId,
    string CountryCode,
    int PaymentTermsDays,
    string ContactEmail,
    SupplierStatus Status,
    long Version,
    int AccountVersion,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? SubmittedBy,
    DateTimeOffset? SubmittedAt,
    Guid? ActivatedBy,
    DateTimeOffset? ActivatedAt,
    string? BlockReason,
    Guid? BlockedBy,
    DateTimeOffset? BlockedAt,
    IReadOnlyList<BankAccountView> BankAccounts)
{
    internal static SupplierView For(Supplier supplier, Actor viewer) => new(
        supplier.Id,
        supplier.LegalName,
        supplier.TaxId.Value,
        supplier.Country.Value,
        supplier.PaymentTermsDays,
        supplier.ContactEmail,
        supplier.Status,
        supplier.Version,
        supplier.AccountVersion,
        supplier.CreatedBy,
        supplier.CreatedAt,
        supplier.SubmittedBy,
        supplier.SubmittedAt,
        supplier.ActivatedBy,
        supplier.ActivatedAt,
        supplier.BlockReason,
        supplier.BlockedBy,
        supplier.BlockedAt,
        [.. supplier.BankAccounts
            .OrderByDescending(static account => account.ProposedAt)
            .ThenByDescending(static account => account.Id)
            .Select(account => BankAccountView.For(account, viewer))]);
}

/// <summary>
/// One entry of a supplier's bank account history, newest first. <see cref="Iban"/> is masked unless
/// <see cref="IbanMasked"/> says otherwise.
/// </summary>
public sealed record BankAccountView(
    Guid Id,
    string Iban,
    bool IbanMasked,
    string Bic,
    string AccountHolder,
    BankAccountStatus Status,
    int? AccountVersion,
    Guid ProposedBy,
    DateTimeOffset ProposedAt,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? RejectionReason)
{
    internal static BankAccountView For(BankAccount account, Actor viewer)
    {
        bool reveal = account.MayRevealIbanTo(viewer);

        return new BankAccountView(
            account.Id,
            reveal ? account.Iban.Value : account.Iban.Masked,
            !reveal,
            account.Bic.Value,
            account.AccountHolder,
            account.Status,
            account.AccountVersion,
            account.ProposedBy,
            account.ProposedAt,
            account.DecidedBy,
            account.DecidedAt,
            account.RejectionReason);
    }
}
