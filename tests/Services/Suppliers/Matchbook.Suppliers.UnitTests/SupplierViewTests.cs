using Matchbook.SharedKernel;
using Matchbook.Suppliers.Application.Features.Suppliers;
using Matchbook.Suppliers.Domain;
using Contract = Matchbook.Contracts.Suppliers;

namespace Matchbook.Suppliers.UnitTests;

/// <summary>What leaves the service: the view a person gets, and the snapshot other services get.</summary>
public sealed class SupplierViewTests
{
    [Fact]
    public void The_approver_reviewing_a_proposal_sees_that_iban_in_full_and_every_other_masked()
    {
        Supplier supplier = Given.Active();
        Given.Propose(supplier, ibanNumber: 7);

        SupplierView view = SupplierView.For(supplier, People.OtherApprover);

        BankAccountView pending = view.BankAccounts.Single(account => account.Status == BankAccountStatus.Pending);
        pending.Iban.ShouldBe(TestIbans.Numbered(7));
        pending.IbanMasked.ShouldBeFalse();

        BankAccountView verified = view.BankAccounts.Single(account => account.Status == BankAccountStatus.Approved);
        verified.Iban.ShouldBe("****3000");
        verified.IbanMasked.ShouldBeTrue();
    }

    [Fact]
    public void The_proposer_and_an_auditor_see_every_iban_masked()
    {
        Supplier supplier = Given.Active();
        Given.Propose(supplier, ibanNumber: 7, proposer: People.AdminAndApprover);

        foreach (Actor viewer in (Actor[])[People.AdminAndApprover, People.Admin, People.Auditor])
        {
            SupplierView view = SupplierView.For(supplier, viewer);

            view.BankAccounts.ShouldAllBe(account => account.IbanMasked && account.Iban.StartsWith("****"));
        }
    }

    [Fact]
    public void Bank_account_history_is_listed_newest_first()
    {
        Supplier supplier = Given.Active();
        BankAccount rejected = Given.Propose(supplier, ibanNumber: 1);
        supplier.RejectBankAccount(People.Approver, rejected.Id, "No", Given.Now);
        BankAccount pending = supplier.ProposeBankAccount(
            People.Admin, Guid.NewGuid(), Iban.Parse(TestIbans.Numbered(2)), Given.Bic, "Acme GmbH", Given.Now.AddHours(2));

        SupplierView view = SupplierView.For(supplier, People.Auditor);

        view.BankAccounts.Select(static account => account.Id)
            .ShouldBe([pending.Id, rejected.Id, supplier.VerifiedAccount.ShouldNotBeNull().Id]);
    }

    [Fact]
    public void The_snapshot_carries_the_account_in_force_with_its_version_and_the_iban_protected()
    {
        Supplier supplier = Given.Active();
        BankAccount second = Given.Propose(supplier, ibanNumber: 2);
        supplier.ApproveBankAccount(People.Approver, second.Id, Given.Now);
        Given.Propose(supplier, ibanNumber: 3);

        Contract.SupplierChanged snapshot = SupplierSnapshot.From(supplier, Given.Now.AddDays(1), MarkingProtector.Instance);

        snapshot.ShouldBe(new Contract.SupplierChanged(
            supplier.Id,
            2,
            "Acme GmbH",
            "DE",
            Contract.SupplierStatus.Active,
            30,
            new Contract.VerifiedBankAccount(
                2, $"protected:{TestIbans.Numbered(2)}", TestIbans.Numbered(2)[^4..], "DEUTDEFF", "Acme GmbH"),
            Given.Now.AddDays(1)));
    }

    [Fact]
    public void A_blocked_supplier_is_published_as_blocked() =>
        SupplierSnapshot.From(Given.Blocked(), Given.Now, MarkingProtector.Instance).Status.ShouldBe(Contract.SupplierStatus.Blocked);

    [Fact]
    public void A_supplier_that_was_never_activated_has_no_snapshot() =>
        Should.Throw<InvalidOperationException>(() => SupplierSnapshot.From(Given.Pending(), Given.Now, MarkingProtector.Instance));
}

/// <summary>Marks what it protects, so a test can see that the IBAN went through the protector.</summary>
internal sealed class MarkingProtector : IFieldProtector
{
    public static readonly MarkingProtector Instance = new();

    public string Protect(string plaintext) => $"protected:{plaintext}";

    public string Unprotect(string stored) => stored["protected:".Length..];
}
