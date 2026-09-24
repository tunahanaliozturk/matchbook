using System.Net.Mail;
using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Domain;

/// <summary>What a supplier admin types in about a supplier, checked. Everything but the bank account.</summary>
public sealed record SupplierDetails
{
    public const int LegalNameMaxLength = 200;
    public const int ContactEmailMaxLength = 254;
    public const int MaxPaymentTermsDays = 120;

    private SupplierDetails(string legalName, TaxId taxId, CountryCode country, int paymentTermsDays, string contactEmail)
    {
        LegalName = legalName;
        TaxId = taxId;
        Country = country;
        PaymentTermsDays = paymentTermsDays;
        ContactEmail = contactEmail;
    }

    public string LegalName { get; }

    public TaxId TaxId { get; }

    public CountryCode Country { get; }

    public int PaymentTermsDays { get; }

    public string ContactEmail { get; }

    public static SupplierDetails Create(
        string? legalName,
        string? taxId,
        string? countryCode,
        int paymentTermsDays,
        string? contactEmail)
    {
        string name = legalName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > LegalNameMaxLength)
        {
            throw new BusinessRuleException(
                "supplier.legal_name_invalid",
                $"A legal name is required and has at most {LegalNameMaxLength} characters.",
                ViolationKind.Invalid);
        }

        if (paymentTermsDays is < 0 or > MaxPaymentTermsDays)
        {
            throw new BusinessRuleException(
                "supplier.payment_terms_invalid",
                $"Payment terms are 0 to {MaxPaymentTermsDays} days.",
                ViolationKind.Invalid);
        }

        return new SupplierDetails(
            name,
            TaxId.Parse(taxId),
            CountryCode.Parse(countryCode),
            paymentTermsDays,
            Email(contactEmail));
    }

    private static string Email(string? input)
    {
        string email = input?.Trim() ?? string.Empty;

        // MailAddress also accepts "Sam <sam@example.com>"; requiring the parsed address to be the whole input
        // keeps display names out of a column that is meant to hold an address.
        return email.Length <= ContactEmailMaxLength
            && MailAddress.TryCreate(email, out MailAddress? address)
            && address.Address == email
                ? email
                : throw new BusinessRuleException(
                    "supplier.email_invalid",
                    "The contact email is not a valid address.",
                    ViolationKind.Invalid);
    }
}
