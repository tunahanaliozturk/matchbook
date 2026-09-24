using System.ComponentModel.DataAnnotations;

namespace Matchbook.Purchasing.Api.Features.PurchaseOrders;

// Request bodies. Every value is nullable and [Required] so a missing field is a 400 naming it, rather than a
// zero that the domain would take at its word: an amendment without a quantity must not set the line to nothing.
// Whether a present value is allowed (below zero, too many decimals, more than is open) is the domain's call and
// comes back as a 422 or 409 with a rule code.

/// <summary>New quantity and unit price for a line of a draft.</summary>
public sealed record AmendLineRequest([Required] decimal? Quantity, [Required] decimal? UnitPrice);

/// <summary>What arrived. Send your own <paramref name="Id"/> to make a retry safe.</summary>
/// <param name="Id">
/// A GUID the client generates. Repeating the request with the same id returns the first receipt; the same id
/// with different content is refused. Leave it out and the server picks one.
/// </param>
/// <param name="Lines">One entry per order line received.</param>
public sealed record RecordReceiptRequest(Guid? Id, [Required] IReadOnlyList<ReceiptLineRequest>? Lines) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Id == Guid.Empty)
        {
            yield return new ValidationResult("The id may be left out, but not empty.", [nameof(Id)]);
        }
    }
}

public sealed record ReceiptLineRequest([Required] int? LineNumber, [Required] decimal? Quantity);
