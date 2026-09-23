using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

/// <summary>
/// The version other services use to discard stale snapshots. It has to rise by exactly one per change they act
/// on, never otherwise, and never before the first activation.
/// </summary>
public sealed class SupplierVersionTests
{
    [Fact]
    public void Nothing_counts_before_the_first_activation()
    {
        Supplier supplier = Given.DraftWithVerifiedAccount();
        supplier.ChangeDetails(People.Admin, Given.Details("Renamed", "AT", 60));
        BankAccount second = Given.Propose(supplier, ibanNumber: 2);
        supplier.ApproveBankAccount(People.Approver, second.Id, Given.Now);
        supplier.Submit(People.Admin, Given.Now);

        supplier.Version.ShouldBe(0);
    }

    [Fact]
    public void The_first_activation_is_version_one() =>
        Given.Active().Version.ShouldBe(1);

    public static TheoryData<string> PublishedChanges => new(PublishedChange.Keys);

    [Theory]
    [MemberData(nameof(PublishedChanges))]
    public void A_change_other_services_act_on_raises_the_version_by_one(string change)
    {
        Supplier supplier = PublishedChange[change].Arrange();

        PublishedChange[change].Act(supplier);

        supplier.Version.ShouldBe(PublishedChange[change].Before + 1);
    }

    [Fact]
    public void Changes_other_services_do_not_see_leave_the_version_alone()
    {
        Supplier supplier = Given.Active();
        SupplierDetails newTaxIdAndEmail =
            SupplierDetails.Create("Acme GmbH", "DE 999 888 777", "DE", 30, "new@acme.example");

        supplier.ChangeDetails(People.Admin, newTaxIdAndEmail);
        BankAccount proposal = Given.Propose(supplier);
        supplier.RejectBankAccount(People.Approver, proposal.Id, "No", Given.Now);
        supplier.ChangeDetails(People.Admin, newTaxIdAndEmail);

        supplier.Version.ShouldBe(1);
    }

    [Fact]
    public void A_refused_change_leaves_the_version_alone()
    {
        Supplier supplier = Given.Active();

        Should.Throw<BusinessRuleException>(() => supplier.Unblock(People.Approver));
        Should.Throw<BusinessRuleException>(() => supplier.Block(People.Admin, "", Given.Now));

        supplier.Version.ShouldBe(1);
    }

    /// <summary>
    /// Runs a random sequence of operations against an active supplier and against a model small enough to be
    /// obviously right, and compares them after every step. Refused operations must change nothing.
    /// </summary>
    [Property(MaxTest = 300)]
    public Property Any_sequence_of_operations_moves_the_versions_exactly_as_the_model_says() =>
        Prop.ForAll(Arb.From(Gen.Elements(Enum.GetValues<Operation>()).ListOf()), operations =>
        {
            Supplier supplier = Given.Active();
            var model = new Model(
                Blocked: false, Pending: null, Verified: TestIbans.German, AccountVersion: 1, Version: 1, Terms: 30);
            int step = 0;

            foreach (Operation operation in operations)
            {
                step++;
                (Model next, bool allowed) = model.After(operation, step);
                Action act = () => Apply(supplier, operation, step);

                if (allowed)
                {
                    act();
                    model = next;
                }
                else
                {
                    Should.Throw<BusinessRuleException>(act);
                }

                string after = $"after step {step}, {operation}";
                supplier.Version.ShouldBe(model.Version, $"version {after}");
                supplier.AccountVersion.ShouldBe(model.AccountVersion, $"account version {after}");
                supplier.Status.ShouldBe(model.Blocked ? SupplierStatus.Blocked : SupplierStatus.Active);
                supplier.PaymentTermsDays.ShouldBe(model.Terms);
                supplier.VerifiedAccount.ShouldNotBeNull().Iban.Value.ShouldBe(model.Verified);
                supplier.PendingAccount?.Iban.Value.ShouldBe(model.Pending);
                (supplier.PendingAccount is null).ShouldBe(model.Pending is null);
            }
        });

    private enum Operation
    {
        Rename,
        ChangeTerms,
        ChangeEmail,
        Block,
        Unblock,
        Propose,
        Approve,
        Reject,
    }

    private static void Apply(Supplier supplier, Operation operation, int step)
    {
        switch (operation)
        {
            case Operation.Rename:
                supplier.ChangeDetails(People.Admin, Given.Details($"Acme {step}", terms: supplier.PaymentTermsDays));
                break;
            case Operation.ChangeTerms:
                int longer = (supplier.PaymentTermsDays + 1) % 121;
                supplier.ChangeDetails(People.Admin, Given.Details(supplier.LegalName, terms: longer));
                break;
            case Operation.ChangeEmail:
                supplier.ChangeDetails(People.Admin, SupplierDetails.Create(
                    supplier.LegalName, "DE123456789", "DE", supplier.PaymentTermsDays, $"ap{step}@acme.example"));
                break;
            case Operation.Block:
                supplier.Block(People.Admin, "Checking", Given.Now);
                break;
            case Operation.Unblock:
                supplier.Unblock(People.Approver);
                break;
            case Operation.Propose:
                Given.Propose(supplier, ibanNumber: 1000 + step);
                break;
            case Operation.Approve:
                supplier.ApproveBankAccount(People.Approver, PendingId(supplier), Given.Now);
                break;
            case Operation.Reject:
                supplier.RejectBankAccount(People.Approver, PendingId(supplier), "No", Given.Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    // With nothing pending, an id nobody holds, so the domain refuses it rather than the test.
    private static Guid PendingId(Supplier supplier) => supplier.PendingAccount?.Id ?? Guid.NewGuid();

    private sealed record Model(
        bool Blocked, string? Pending, string Verified, int AccountVersion, long Version, int Terms)
    {
        public (Model Next, bool Allowed) After(Operation operation, int step) => operation switch
        {
            Operation.Rename => (this with { Version = Version + 1 }, true),
            Operation.ChangeTerms => (this with { Terms = (Terms + 1) % 121, Version = Version + 1 }, true),
            Operation.ChangeEmail => (this, true),
            Operation.Block => (this with { Blocked = true, Version = Version + 1 }, !Blocked),
            Operation.Unblock => (this with { Blocked = false, Version = Version + 1 }, Blocked),
            Operation.Propose => (this with { Pending = TestIbans.Numbered(1000 + step) }, Pending is null),
            Operation.Approve => (
                this with
                {
                    Verified = Pending!,
                    Pending = null,
                    AccountVersion = AccountVersion + 1,
                    Version = Version + 1,
                },
                Pending is not null),
            Operation.Reject => (this with { Pending = null }, Pending is not null),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private sealed record Case(Func<Supplier> Arrange, Action<Supplier> Act, long Before);

    private static readonly Dictionary<string, Case> PublishedChange = new(StringComparer.Ordinal)
    {
        ["block"] = new(Given.Active, static s => s.Block(People.Admin, "Checking", Given.Now), 1),
        ["unblock"] = new(Given.Blocked, static s => s.Unblock(People.Approver), 2),
        ["new verified account"] = new(Given.Active, ApproveNewAccount, 1),
        ["new verified account while blocked"] = new(Given.Blocked, ApproveNewAccount, 2),
        ["legal name"] = new(Given.Active, static s => Change(s, Given.Details("Acme Holding GmbH")), 1),
        ["payment terms"] = new(Given.Active, static s => Change(s, Given.Details(terms: 60)), 1),
        ["country"] = new(Given.Active, static s => Change(s, Given.Details(country: "AT")), 1),
        ["name and terms at once"] = new(Given.Active, static s => Change(s, Given.Details("Acme AG", terms: 14)), 1),
    };

    private static void ApproveNewAccount(Supplier supplier) =>
        supplier.ApproveBankAccount(People.Approver, Given.Propose(supplier).Id, Given.Now);

    private static void Change(Supplier supplier, SupplierDetails details) =>
        supplier.ChangeDetails(People.Admin, details);
}
