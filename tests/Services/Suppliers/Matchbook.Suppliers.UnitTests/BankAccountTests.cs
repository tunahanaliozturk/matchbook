using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

public sealed class BankAccountTests
{
    [Fact]
    public void A_proposal_waits_for_approval_and_the_verified_account_stays_in_force()
    {
        Supplier supplier = Given.Active();
        BankAccount verified = supplier.VerifiedAccount.ShouldNotBeNull();

        BankAccount proposal = Given.Propose(supplier, ibanNumber: 7);

        proposal.Status.ShouldBe(BankAccountStatus.Pending);
        proposal.ProposedBy.ShouldBe(People.Admin.Id);
        proposal.Iban.Value.ShouldBe(TestIbans.Numbered(7));
        supplier.PendingAccount.ShouldBe(proposal);
        supplier.VerifiedAccount.ShouldBe(verified);
        supplier.AccountVersion.ShouldBe(1);
    }

    [Fact]
    public void Approval_by_someone_else_puts_the_account_in_force_and_raises_the_account_version()
    {
        Supplier supplier = Given.Active();
        BankAccount proposal = Given.Propose(supplier);

        supplier.ApproveBankAccount(People.OtherApprover, proposal.Id, Given.Now.AddDays(1));

        supplier.VerifiedAccount.ShouldBe(proposal);
        supplier.PendingAccount.ShouldBeNull();
        supplier.AccountVersion.ShouldBe(2);
        proposal.Status.ShouldBe(BankAccountStatus.Approved);
        proposal.AccountVersion.ShouldBe(2);
        proposal.DecidedBy.ShouldBe(People.OtherApprover.Id);
        proposal.DecidedAt.ShouldBe(Given.Now.AddDays(1));
    }

    [Fact]
    public void The_proposer_cannot_approve_their_own_account_even_holding_the_approver_role()
    {
        Supplier supplier = Given.Active();
        BankAccount proposal = Given.Propose(supplier, proposer: People.AdminAndApprover);

        BrokenRule.Expect("supplier.self_approval", ViolationKind.Forbidden, () =>
            supplier.ApproveBankAccount(People.AdminAndApprover, proposal.Id, Given.Now));
        proposal.Status.ShouldBe(BankAccountStatus.Pending);
        supplier.AccountVersion.ShouldBe(1);
    }

    [Fact]
    public void A_supplier_admin_cannot_approve_a_bank_account()
    {
        Supplier supplier = Given.Active();
        BankAccount proposal = Given.Propose(supplier);

        BrokenRule.Expect("supplier.role_required", ViolationKind.Forbidden, () =>
            supplier.ApproveBankAccount(People.OtherAdmin, proposal.Id, Given.Now));
    }

    [Fact]
    public void A_supplier_approver_cannot_propose_a_bank_account() =>
        BrokenRule.Expect("supplier.role_required", ViolationKind.Forbidden, () =>
            Given.Propose(Given.Active(), proposer: People.Approver));

    [Fact]
    public void Rejection_keeps_the_verified_account_and_records_who_and_why()
    {
        Supplier supplier = Given.Active();
        BankAccount verified = supplier.VerifiedAccount.ShouldNotBeNull();
        BankAccount proposal = Given.Propose(supplier);

        supplier.RejectBankAccount(People.Approver, proposal.Id, " Letter not on company paper ", Given.Now.AddDays(1));

        proposal.Status.ShouldBe(BankAccountStatus.Rejected);
        proposal.RejectionReason.ShouldBe("Letter not on company paper");
        proposal.DecidedBy.ShouldBe(People.Approver.Id);
        proposal.DecidedAt.ShouldBe(Given.Now.AddDays(1));
        proposal.AccountVersion.ShouldBeNull();
        supplier.VerifiedAccount.ShouldBe(verified);
        supplier.AccountVersion.ShouldBe(1);
    }

    [Fact]
    public void The_proposer_may_withdraw_a_proposal_by_rejecting_it()
    {
        Supplier supplier = Given.Active();
        BankAccount proposal = Given.Propose(supplier);

        supplier.RejectBankAccount(People.Admin, proposal.Id, "Typed the wrong IBAN", Given.Now);

        proposal.Status.ShouldBe(BankAccountStatus.Rejected);
    }

    [Fact]
    public void A_rejection_needs_a_reason()
    {
        Supplier supplier = Given.Active();
        BankAccount proposal = Given.Propose(supplier);

        BrokenRule.Expect("supplier.reason_invalid", ViolationKind.Invalid, () =>
            supplier.RejectBankAccount(People.Approver, proposal.Id, " ", Given.Now));
        proposal.Status.ShouldBe(BankAccountStatus.Pending);
    }

    [Fact]
    public void A_second_proposal_waits_until_the_first_is_decided()
    {
        Supplier supplier = Given.Active();
        BankAccount first = Given.Propose(supplier, ibanNumber: 1);

        BrokenRule.Expect("supplier.bank_account_pending", ViolationKind.Conflict, () =>
            Given.Propose(supplier, ibanNumber: 2));

        supplier.RejectBankAccount(People.Approver, first.Id, "Wrong account", Given.Now);
        Given.Propose(supplier, ibanNumber: 2).Status.ShouldBe(BankAccountStatus.Pending);
    }

    [Fact]
    public void Proposing_the_account_already_in_force_is_refused_however_it_is_typed()
    {
        Supplier supplier = Given.Active();
        Iban sameIban = Iban.Parse("de89 3704 0044 0532 0130 00");
        Bic sameBic = Bic.Parse("deutdeff");

        BrokenRule.Expect("supplier.bank_account_unchanged", ViolationKind.Conflict, () =>
            supplier.ProposeBankAccount(People.Admin, Guid.NewGuid(), sameIban, sameBic, " Acme GmbH ", Given.Now));
    }

    [Fact]
    public void The_same_iban_under_a_new_holder_name_is_a_change()
    {
        Supplier supplier = Given.Active();

        BankAccount proposal = supplier.ProposeBankAccount(
            People.Admin, Guid.CreateVersion7(), Iban.Parse(TestIbans.German), Given.Bic, "Acme Europe GmbH", Given.Now);

        proposal.Status.ShouldBe(BankAccountStatus.Pending);
    }

    [Fact]
    public void A_decided_proposal_cannot_be_decided_again()
    {
        Supplier supplier = Given.Active();
        BankAccount approved = supplier.VerifiedAccount.ShouldNotBeNull();
        BankAccount rejected = Given.Propose(supplier);
        supplier.RejectBankAccount(People.Approver, rejected.Id, "No", Given.Now);

        BrokenRule.Expect("supplier.bank_account_not_pending", ViolationKind.Conflict, () =>
            supplier.ApproveBankAccount(People.OtherApprover, approved.Id, Given.Now));
        BrokenRule.Expect("supplier.bank_account_not_pending", ViolationKind.Conflict, () =>
            supplier.ApproveBankAccount(People.OtherApprover, rejected.Id, Given.Now));
        BrokenRule.Expect("supplier.bank_account_not_pending", ViolationKind.Conflict, () =>
            supplier.RejectBankAccount(People.OtherApprover, approved.Id, "No", Given.Now));
        supplier.AccountVersion.ShouldBe(1);
    }

    [Fact]
    public void An_account_of_another_supplier_is_not_found()
    {
        Supplier other = Given.Active();
        BankAccount elsewhere = Given.Propose(other);

        BrokenRule.Expect("supplier.bank_account_not_found", ViolationKind.NotFound, () =>
            Given.Active().ApproveBankAccount(People.Approver, elsewhere.Id, Given.Now));
    }

    [Fact]
    public void History_keeps_every_account_with_who_proposed_and_who_decided()
    {
        Supplier supplier = Given.Active();
        BankAccount rejected = Given.Propose(supplier, ibanNumber: 1);
        supplier.RejectBankAccount(People.Approver, rejected.Id, "Wrong", Given.Now);
        BankAccount second = Given.Propose(supplier, ibanNumber: 2, proposer: People.OtherAdmin);
        supplier.ApproveBankAccount(People.OtherApprover, second.Id, Given.Now);
        BankAccount pending = Given.Propose(supplier, ibanNumber: 3);

        supplier.BankAccounts
            .Select(static account => (account.Status, account.AccountVersion, account.ProposedBy, account.DecidedBy))
            .ShouldBe(
        [
            (BankAccountStatus.Approved, 1, People.Admin.Id, People.Approver.Id),
            (BankAccountStatus.Rejected, null, People.Admin.Id, People.Approver.Id),
            (BankAccountStatus.Approved, 2, People.OtherAdmin.Id, People.OtherApprover.Id),
            (BankAccountStatus.Pending, null, People.Admin.Id, null),
        ]);
        supplier.VerifiedAccount.ShouldBe(second);
        supplier.PendingAccount.ShouldBe(pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_account_holder_name_is_required(string? holder) =>
        BrokenRule.Expect("supplier.account_holder_invalid", ViolationKind.Invalid, () => ProposeHeldBy(holder));

    [Fact]
    public void An_account_holder_name_fits_a_sepa_credit_transfer()
    {
        ProposeHeldBy(new string('a', 70)).AccountHolder.Length.ShouldBe(70);

        BrokenRule.Expect("supplier.account_holder_invalid", ViolationKind.Invalid, () =>
            ProposeHeldBy(new string('a', 71)));
    }

    [Fact]
    public void Only_an_approver_who_did_not_propose_it_sees_a_pending_iban_in_full()
    {
        Supplier supplier = Given.Active();
        BankAccount pending = Given.Propose(supplier, proposer: People.AdminAndApprover);

        pending.MayRevealIbanTo(People.Approver).ShouldBeTrue();
        pending.MayRevealIbanTo(People.OtherApprover).ShouldBeTrue();
        pending.MayRevealIbanTo(People.AdminAndApprover).ShouldBeFalse();
        pending.MayRevealIbanTo(People.Admin).ShouldBeFalse();
        pending.MayRevealIbanTo(People.Auditor).ShouldBeFalse();
    }

    [Fact]
    public void Once_decided_an_iban_is_masked_for_everyone()
    {
        Supplier supplier = Given.Active();
        BankAccount verified = supplier.VerifiedAccount.ShouldNotBeNull();
        BankAccount rejected = Given.Propose(supplier);
        supplier.RejectBankAccount(People.Approver, rejected.Id, "No", Given.Now);

        foreach (Actor actor in (Actor[])[People.Admin, People.Approver, People.OtherApprover, People.Auditor])
        {
            verified.MayRevealIbanTo(actor).ShouldBeFalse();
            rejected.MayRevealIbanTo(actor).ShouldBeFalse();
        }
    }

    private static BankAccount ProposeHeldBy(string? holder) =>
        Given.Draft().ProposeBankAccount(
            People.Admin, Guid.CreateVersion7(), Iban.Parse(TestIbans.German), Given.Bic, holder, Given.Now);
}
