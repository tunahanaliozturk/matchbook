using Matchbook.Payables.Domain.Suppliers;

namespace Matchbook.Payables.Domain.PaymentRuns;

/// <summary>
/// One supplier in a payment run, with the verified account the run will pay into as it stood at the draft. If the
/// supplier's account version is still the same at release, this is still the account on record, so the bank file
/// is written from here and stays the same however often it is downloaded. The account number stays encrypted; it
/// is decrypted only while the file is written.
/// </summary>
public sealed class PaymentRunCreditor
{
    private PaymentRunCreditor(
        Guid supplierId,
        int accountVersion,
        string accountHolder,
        string protectedIban,
        string ibanLastFour,
        Bic bic,
        int itemCount,
        decimal total,
        CreditorStatus status)
    {
        SupplierId = supplierId;
        AccountVersion = accountVersion;
        AccountHolder = accountHolder;
        ProtectedIban = protectedIban;
        IbanLastFour = ibanLastFour;
        Bic = bic;
        ItemCount = itemCount;
        Total = total;
        Status = status;
    }

    public Guid SupplierId { get; private set; }

    public int AccountVersion { get; private set; }

    public string AccountHolder { get; private set; }

    public string ProtectedIban { get; private set; }

    public string IbanLastFour { get; private set; }

    public Bic Bic { get; private set; }

    public int ItemCount { get; private set; }

    public decimal Total { get; private set; }

    public CreditorStatus Status { get; private set; }

    public CreditorDropReason? DropReason { get; private set; }

    internal static PaymentRunCreditor Schedule(Supplier supplier, int itemCount, decimal total)
    {
        SupplierAccount account = supplier.Account
            ?? throw new InvalidOperationException("A supplier without a verified account cannot be scheduled.");

        return new PaymentRunCreditor(
            supplier.Id,
            account.AccountVersion,
            account.AccountHolder,
            account.ProtectedIban,
            account.IbanLastFour,
            account.Bic,
            itemCount,
            total,
            CreditorStatus.Scheduled);
    }

    /// <summary>Why this creditor cannot be paid at release, measured against the supplier as it is now; null if it can.</summary>
    internal CreditorDropReason? DropReasonAgainst(Supplier? current)
    {
        if (current is not { IsActive: true })
        {
            return CreditorDropReason.SupplierNotActive;
        }

        return current.Account?.AccountVersion == AccountVersion ? null : CreditorDropReason.AccountChanged;
    }

    internal void Pay() => Status = CreditorStatus.Paid;

    internal void Drop(CreditorDropReason reason)
    {
        Status = CreditorStatus.Dropped;
        DropReason = reason;
    }
}

public enum CreditorStatus
{
    Scheduled,
    Paid,
    Dropped,
}

public enum CreditorDropReason
{
    SupplierNotActive,
    AccountChanged,
}
