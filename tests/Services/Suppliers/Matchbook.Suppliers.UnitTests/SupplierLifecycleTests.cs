using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

public sealed class SupplierLifecycleTests
{
    private const string RoleRequired = "supplier.role_required";
    private const string InvalidTransition = "supplier.invalid_transition";

    [Fact]
    public void A_new_supplier_is_a_draft_that_records_who_created_it()
    {
        Supplier supplier = Supplier.Create(People.Admin, Given.Details(), Given.Now);

        supplier.Status.ShouldBe(SupplierStatus.Draft);
        supplier.CreatedBy.ShouldBe(People.Admin.Id);
        supplier.CreatedAt.ShouldBe(Given.Now);
        supplier.LegalName.ShouldBe("Acme GmbH");
        supplier.Id.Version.ShouldBe(7);
        supplier.VerifiedAccount.ShouldBeNull();
    }

    [Fact]
    public void Only_a_supplier_admin_creates_a_supplier()
    {
        foreach (Actor actor in (Actor[])[People.Approver, People.Auditor])
        {
            BrokenRule.Expect(RoleRequired, ViolationKind.Forbidden, () => Supplier.Create(actor, Given.Details(), Given.Now));
        }
    }

    [Fact]
    public void Submitting_moves_a_draft_to_pending_activation_and_records_who_submitted()
    {
        Supplier supplier = Given.Draft();

        supplier.Submit(People.OtherAdmin, Given.Now.AddDays(1));

        supplier.Status.ShouldBe(SupplierStatus.PendingActivation);
        supplier.SubmittedBy.ShouldBe(People.OtherAdmin.Id);
        supplier.SubmittedAt.ShouldBe(Given.Now.AddDays(1));
    }

    [Fact]
    public void A_supplier_approver_cannot_submit() =>
        BrokenRule.Expect(RoleRequired, ViolationKind.Forbidden, () => Given.Draft().Submit(People.Approver, Given.Now));

    [Theory]
    [InlineData(SupplierStatus.PendingActivation)]
    [InlineData(SupplierStatus.Active)]
    [InlineData(SupplierStatus.Blocked)]
    public void Only_a_draft_can_be_submitted(SupplierStatus status) =>
        BrokenRule.Expect(InvalidTransition, ViolationKind.Conflict, () => Given.In(status).Submit(People.Admin, Given.Now));

    [Fact]
    public void An_approver_who_did_not_submit_activates_a_pending_supplier()
    {
        Supplier supplier = Given.Pending();

        supplier.Activate(People.OtherApprover, Given.Now.AddDays(2));

        supplier.Status.ShouldBe(SupplierStatus.Active);
        supplier.ActivatedBy.ShouldBe(People.OtherApprover.Id);
        supplier.ActivatedAt.ShouldBe(Given.Now.AddDays(2));
    }

    [Fact]
    public void The_person_who_submitted_cannot_activate_even_holding_the_approver_role()
    {
        Supplier supplier = Given.DraftWithVerifiedAccount();
        supplier.Submit(People.AdminAndApprover, Given.Now);

        BrokenRule.Expect("supplier.self_approval", ViolationKind.Forbidden, () => supplier.Activate(People.AdminAndApprover, Given.Now));
        supplier.Status.ShouldBe(SupplierStatus.PendingActivation);
    }

    [Fact]
    public void A_supplier_admin_cannot_activate() =>
        BrokenRule.Expect(RoleRequired, ViolationKind.Forbidden, () => Given.Pending().Activate(People.OtherAdmin, Given.Now));

    [Fact]
    public void Activation_needs_an_approved_bank_account_and_a_pending_proposal_is_not_one()
    {
        Supplier supplier = Given.Draft();
        Given.Propose(supplier);
        supplier.Submit(People.Admin, Given.Now);

        BrokenRule.Expect("supplier.no_verified_account", ViolationKind.Conflict, () => supplier.Activate(People.Approver, Given.Now));
        supplier.Status.ShouldBe(SupplierStatus.PendingActivation);
    }

    [Theory]
    [InlineData(SupplierStatus.Draft)]
    [InlineData(SupplierStatus.Active)]
    [InlineData(SupplierStatus.Blocked)]
    public void Only_a_pending_supplier_can_be_activated(SupplierStatus status) =>
        BrokenRule.Expect(InvalidTransition, ViolationKind.Conflict, () => Given.In(status).Activate(People.OtherApprover, Given.Now));

    [Fact]
    public void Either_role_can_block_an_active_supplier_and_the_reason_is_kept()
    {
        foreach (Actor actor in (Actor[])[People.Admin, People.Approver])
        {
            Supplier supplier = Given.Active();

            supplier.Block(actor, "  Bank details reported as fraudulent  ", Given.Now.AddDays(3));

            supplier.Status.ShouldBe(SupplierStatus.Blocked);
            supplier.BlockReason.ShouldBe("Bank details reported as fraudulent");
            supplier.BlockedBy.ShouldBe(actor.Id);
            supplier.BlockedAt.ShouldBe(Given.Now.AddDays(3));
        }
    }

    [Fact]
    public void An_auditor_cannot_block() =>
        BrokenRule.Expect(RoleRequired, ViolationKind.Forbidden, () => Given.Active().Block(People.Auditor, "No", Given.Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blocking_needs_a_reason(string? reason)
    {
        Supplier supplier = Given.Active();

        BrokenRule.Expect("supplier.reason_invalid", ViolationKind.Invalid, () => supplier.Block(People.Admin, reason, Given.Now));
        supplier.Status.ShouldBe(SupplierStatus.Active);
    }

    [Fact]
    public void A_block_reason_has_at_most_500_characters()
    {
        BrokenRule.Expect("supplier.reason_invalid", ViolationKind.Invalid, () => Given.Active().Block(People.Admin, new string('x', 501), Given.Now));

        Supplier supplier = Given.Active();
        supplier.Block(People.Admin, new string('x', 500), Given.Now);
        supplier.Status.ShouldBe(SupplierStatus.Blocked);
    }

    [Theory]
    [InlineData(SupplierStatus.Draft)]
    [InlineData(SupplierStatus.PendingActivation)]
    [InlineData(SupplierStatus.Blocked)]
    public void Only_an_active_supplier_can_be_blocked(SupplierStatus status) =>
        BrokenRule.Expect(InvalidTransition, ViolationKind.Conflict, () => Given.In(status).Block(People.Admin, "Reason", Given.Now));

    [Fact]
    public void Unblocking_returns_the_supplier_to_active_and_clears_the_block()
    {
        Supplier supplier = Given.Blocked();

        supplier.Unblock(People.Approver);

        supplier.Status.ShouldBe(SupplierStatus.Active);
        supplier.BlockReason.ShouldBeNull();
        supplier.BlockedBy.ShouldBeNull();
        supplier.BlockedAt.ShouldBeNull();
    }

    [Fact]
    public void Only_a_supplier_approver_unblocks() =>
        BrokenRule.Expect(RoleRequired, ViolationKind.Forbidden, () => Given.Blocked().Unblock(People.Admin));

    [Theory]
    [InlineData(SupplierStatus.Draft)]
    [InlineData(SupplierStatus.PendingActivation)]
    [InlineData(SupplierStatus.Active)]
    public void Only_a_blocked_supplier_can_be_unblocked(SupplierStatus status) =>
        BrokenRule.Expect(InvalidTransition, ViolationKind.Conflict, () => Given.In(status).Unblock(People.Approver));

    [Theory]
    [InlineData(SupplierStatus.Draft)]
    [InlineData(SupplierStatus.PendingActivation)]
    [InlineData(SupplierStatus.Active)]
    [InlineData(SupplierStatus.Blocked)]
    public void Details_can_be_changed_in_every_state_without_changing_the_state(SupplierStatus status)
    {
        Supplier supplier = Given.In(status);
        SupplierDetails details = SupplierDetails.Create("Acme Europe B.V.", "NL854729345B01", "NL", 45, "finance@acme.example");

        supplier.ChangeDetails(People.OtherAdmin, details);

        supplier.Status.ShouldBe(status);
        supplier.LegalName.ShouldBe("Acme Europe B.V.");
        supplier.TaxId.Value.ShouldBe("NL854729345B01");
        supplier.Country.Value.ShouldBe("NL");
        supplier.PaymentTermsDays.ShouldBe(45);
        supplier.ContactEmail.ShouldBe("finance@acme.example");
    }

    [Fact]
    public void Only_a_supplier_admin_changes_details() =>
        BrokenRule.Expect(RoleRequired, ViolationKind.Forbidden, () => Given.Active().ChangeDetails(People.Approver, Given.Details("Other")));
}
