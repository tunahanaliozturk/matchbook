using System.ComponentModel;
using Matchbook.Suppliers.Application.Features.Suppliers;
using Matchbook.Suppliers.Application.Features.Suppliers.Queries.ListSuppliers;
using Domain = Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Api.Features.Suppliers;

// Response bodies. They mirror the application's views on purpose: the HTTP contract is what a generated client
// depends on, and it should change only when someone edits this file, not whenever the domain renames a state.

public enum SupplierStatus
{
    Draft,
    PendingActivation,
    Active,
    Blocked,
}

public enum BankAccountStatus
{
    Pending,
    Approved,
    Rejected,
}

public sealed record SupplierResponse(
    Guid Id,
    string LegalName,
    string TaxId,
    string CountryCode,
    int PaymentTermsDays,
    string ContactEmail,
    SupplierStatus Status,
    [property: Description("Rises by one with every change other services are told about; 0 before activation.")]
    long Version,
    [property: Description("How many bank accounts have been approved. The approved account with this version is in force.")]
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
    [property: Description("Every account ever proposed, newest first.")]
    IReadOnlyList<BankAccountResponse> BankAccounts)
{
    internal static SupplierResponse From(SupplierView supplier) => new(
        supplier.Id,
        supplier.LegalName,
        supplier.TaxId,
        supplier.CountryCode,
        supplier.PaymentTermsDays,
        supplier.ContactEmail,
        Statuses.From(supplier.Status),
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
        [.. supplier.BankAccounts.Select(BankAccountResponse.From)]);
}

public sealed record BankAccountResponse(
    Guid Id,
    [property: Description("Masked to the last four characters, except for a supplier approver reviewing this account while it is pending.")]
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
    internal static BankAccountResponse From(BankAccountView account) => new(
        account.Id,
        account.Iban,
        account.IbanMasked,
        account.Bic,
        account.AccountHolder,
        Statuses.From(account.Status),
        account.AccountVersion,
        account.ProposedBy,
        account.ProposedAt,
        account.DecidedBy,
        account.DecidedAt,
        account.RejectionReason);
}

public sealed record SupplierSummaryResponse(
    Guid Id,
    string LegalName,
    string TaxId,
    string CountryCode,
    SupplierStatus Status,
    int PaymentTermsDays,
    int AccountVersion,
    bool HasPendingBankAccount)
{
    internal static SupplierSummaryResponse From(SupplierSummary supplier) => new(
        supplier.Id,
        supplier.LegalName,
        supplier.TaxId,
        supplier.CountryCode,
        Statuses.From(supplier.Status),
        supplier.PaymentTermsDays,
        supplier.AccountVersion,
        supplier.HasPendingBankAccount);
}

public sealed record SupplierPageResponse(
    IReadOnlyList<SupplierSummaryResponse> Items,
    [property: Description("Pass as after= for the next page; null on the last page.")]
    Guid? Next)
{
    internal static SupplierPageResponse From(SupplierPage page) =>
        new([.. page.Items.Select(SupplierSummaryResponse.From)], page.Next);
}

internal static class Statuses
{
    public static SupplierStatus From(Domain.SupplierStatus status) => status switch
    {
        Domain.SupplierStatus.Draft => SupplierStatus.Draft,
        Domain.SupplierStatus.PendingActivation => SupplierStatus.PendingActivation,
        Domain.SupplierStatus.Active => SupplierStatus.Active,
        Domain.SupplierStatus.Blocked => SupplierStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A state the HTTP contract does not know."),
    };

    public static Domain.SupplierStatus ToDomain(SupplierStatus status) => status switch
    {
        SupplierStatus.Draft => Domain.SupplierStatus.Draft,
        SupplierStatus.PendingActivation => Domain.SupplierStatus.PendingActivation,
        SupplierStatus.Active => Domain.SupplierStatus.Active,
        SupplierStatus.Blocked => Domain.SupplierStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a supplier state."),
    };

    public static BankAccountStatus From(Domain.BankAccountStatus status) => status switch
    {
        Domain.BankAccountStatus.Pending => BankAccountStatus.Pending,
        Domain.BankAccountStatus.Approved => BankAccountStatus.Approved,
        Domain.BankAccountStatus.Rejected => BankAccountStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A state the HTTP contract does not know."),
    };
}
