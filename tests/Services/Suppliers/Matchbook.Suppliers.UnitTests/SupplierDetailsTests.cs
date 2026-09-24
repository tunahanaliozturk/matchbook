using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.UnitTests;

public sealed class SupplierDetailsTests
{
    [Fact]
    public void Details_are_trimmed_and_normalised()
    {
        SupplierDetails details = SupplierDetails.Create("  Acme GmbH ", "de 123 456 789", "de", 30, " ap@acme.example ");

        details.LegalName.ShouldBe("Acme GmbH");
        details.TaxId.Value.ShouldBe("DE123456789");
        details.Country.Value.ShouldBe("DE");
        details.PaymentTermsDays.ShouldBe(30);
        details.ContactEmail.ShouldBe("ap@acme.example");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_legal_name_is_required(string? name) =>
        BrokenRule.Expect("supplier.legal_name_invalid", ViolationKind.Invalid, () => Create(name: name));

    [Fact]
    public void A_legal_name_has_at_most_200_characters()
    {
        Create(name: new string('a', 200)).LegalName.Length.ShouldBe(200);
        BrokenRule.Expect("supplier.legal_name_invalid", ViolationKind.Invalid, () =>
            Create(name: new string('a', 201)));
    }

    [Property]
    public Property Payment_terms_are_accepted_exactly_from_0_to_120_days() =>
        Prop.ForAll(Arb.From(Gen.Choose(-1000, 1000)), days =>
        {
            if (days is >= 0 and <= 120)
            {
                Create(terms: days).PaymentTermsDays.ShouldBe(days);
            }
            else
            {
                BrokenRule.Expect("supplier.payment_terms_invalid", ViolationKind.Invalid, () => Create(terms: days));
            }
        });

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    [InlineData(int.MinValue)]
    public void Payment_terms_just_outside_the_range_are_refused(int days) =>
        BrokenRule.Expect("supplier.payment_terms_invalid", ViolationKind.Invalid, () => Create(terms: days));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("Acme AP <ap@acme.example>")]
    [InlineData("ap@acme.example, ar@acme.example")]
    public void A_contact_email_must_be_one_plain_address(string? email) =>
        BrokenRule.Expect("supplier.email_invalid", ViolationKind.Invalid, () => Create(email: email));

    [Fact]
    public void A_contact_email_has_at_most_254_characters()
    {
        string label = new('b', 61);
        string longest = $"{new string('a', 64)}@{label}.{label}.{label}.exa";
        longest.Length.ShouldBe(254);

        Create(email: longest).ContactEmail.ShouldBe(longest);
        BrokenRule.Expect("supplier.email_invalid", ViolationKind.Invalid, () => Create(email: "a" + longest));
    }

    private static SupplierDetails Create(
        string? name = "Acme GmbH", int terms = 30, string? email = "ap@acme.example") =>
        SupplierDetails.Create(name, "DE123456789", "DE", terms, email);
}
