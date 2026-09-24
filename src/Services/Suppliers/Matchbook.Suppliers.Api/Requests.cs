using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Matchbook.Suppliers.Application;

namespace Matchbook.Suppliers.Api;

// Request bodies. The attributes check only shape: a field is present. Whether a value is acceptable (a tax id,
// an IBAN, 0 to 120 days) is the domain's call, answered with 422 and a code.

public sealed record CreateSupplierRequest(
    [property: Description("Optional. A GUID the client chooses; sending the same request again with it returns the first supplier instead of a second.")]
    Guid? Id,
    [Required] string? LegalName,
    [property: Description("Separators and case do not matter: DE 123.456.789 and de123456789 are the same supplier.")]
    [Required] string? TaxId,
    [property: Description("ISO 3166-1 alpha-2.")]
    [Required] string? CountryCode,
    [property: Description("0 to 120.")]
    [Required] int? PaymentTermsDays,
    [Required] string? ContactEmail)
{
    internal CreateSupplier ToCommand() =>
        new(Id, LegalName!, TaxId!, CountryCode!, PaymentTermsDays!.Value, ContactEmail!);
}

public sealed record ChangeSupplierDetailsRequest(
    [Required] string? LegalName,
    [Required] string? TaxId,
    [Required] string? CountryCode,
    [Required] int? PaymentTermsDays,
    [Required] string? ContactEmail)
{
    internal ChangeSupplierDetails ToCommand(Guid supplierId) =>
        new(supplierId, LegalName!, TaxId!, CountryCode!, PaymentTermsDays!.Value, ContactEmail!);
}

public sealed record ReasonRequest([Required] string? Reason);

public sealed record ProposeBankAccountRequest(
    [property: Description("Optional. A GUID the client chooses for the proposal; sending the same request again with it does not propose twice.")]
    Guid? Id,
    [property: Description("Spaces and case do not matter. Only SEPA countries.")]
    [Required] string? Iban,
    [Required] string? Bic,
    [property: Description("As the bank has it, at most 70 characters.")]
    [Required] string? AccountHolder)
{
    internal ProposeBankAccount ToCommand(Guid supplierId) => new(supplierId, Id, Iban!, Bic!, AccountHolder!);
}
