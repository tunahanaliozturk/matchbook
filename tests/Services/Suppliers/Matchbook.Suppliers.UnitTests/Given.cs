using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

/// <summary>Suppliers in each state, reached through the real transitions rather than set up field by field.</summary>
internal static class Given
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public static readonly Bic Bic = Bic.Parse("DEUTDEFF");

    public static SupplierDetails Details(string name = "Acme GmbH", string country = "DE", int terms = 30) =>
        SupplierDetails.Create(name, "DE123456789", country, terms, "ap@acme.example");

    public static Supplier Draft() => Supplier.Create(People.Admin, Guid.CreateVersion7(), Details(), Now);

    public static Supplier DraftWithVerifiedAccount()
    {
        Supplier supplier = Draft();
        BankAccount account = supplier.ProposeBankAccount(
            People.Admin, Guid.CreateVersion7(), Iban.Parse(TestIbans.German), Bic, "Acme GmbH", Now);
        supplier.ApproveBankAccount(People.Approver, account.Id, Now.AddMinutes(5));
        return supplier;
    }

    public static Supplier Pending()
    {
        Supplier supplier = DraftWithVerifiedAccount();
        supplier.Submit(People.Admin, Now.AddMinutes(10));
        return supplier;
    }

    public static Supplier Active()
    {
        Supplier supplier = Pending();
        supplier.Activate(People.Approver, Now.AddMinutes(15));
        return supplier;
    }

    public static Supplier Blocked()
    {
        Supplier supplier = Active();
        supplier.Block(People.Admin, "Suspected invoice fraud", Now.AddMinutes(20));
        return supplier;
    }

    public static Supplier In(SupplierStatus status) => status switch
    {
        SupplierStatus.Draft => Draft(),
        SupplierStatus.PendingActivation => Pending(),
        SupplierStatus.Active => Active(),
        SupplierStatus.Blocked => Blocked(),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static BankAccount Propose(Supplier supplier, int ibanNumber = 1, Actor? proposer = null) =>
        supplier.ProposeBankAccount(
            proposer ?? People.Admin,
            Guid.CreateVersion7(),
            Iban.Parse(TestIbans.Numbered(ibanNumber)),
            Bic,
            "Acme GmbH",
            Now.AddHours(1));
}
