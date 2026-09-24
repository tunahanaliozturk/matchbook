using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.Api;

// Requests check only their shape here (a 400 names the missing field). Every rule about the content, from
// line counts to scales to dates, is the domain's and comes back as a 422 with a code.

public sealed record LineRequest(
    [property: Required] string Description,
    [property: Description("Up to three decimal places.")] decimal Quantity,
    [property: Required, Description("For example EA, KG, H.")] string UnitOfMeasure,
    [property: Description("Estimated, in euros, up to four decimal places.")] decimal UnitPrice);

public sealed record CreateRequisitionRequest(
    [property: Description("Optional. A GUID the client generates. Sending the same request again with the same id returns what the first one created; a different request with it is a 409 request.id_reused.")]
    Guid? Id,
    [property: Required] string CostCentreCode,
    Guid SupplierId,
    [property: Required] string Justification,
    DateOnly NeededBy,
    [property: Required, Description("One to fifty lines.")] IReadOnlyList<LineRequest> Lines) : IValidatableObject
{
    internal RequisitionDetails ToDetails() => Details.From(CostCentreCode, SupplierId, Justification, NeededBy, Lines);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Id == Guid.Empty)
        {
            yield return new ValidationResult("An id, when sent, must not be the empty GUID.", [nameof(Id)]);
        }
    }
}

public sealed record EditRequisitionRequest(
    [property: Required] string CostCentreCode,
    Guid SupplierId,
    [property: Required] string Justification,
    DateOnly NeededBy,
    [property: Required, Description("One to fifty lines. They replace the draft's lines.")] IReadOnlyList<LineRequest> Lines)
{
    internal RequisitionDetails ToDetails() => Details.From(CostCentreCode, SupplierId, Justification, NeededBy, Lines);
}

public sealed record RejectRequisitionRequest([property: Required] string Reason);

internal static class Details
{
    public static RequisitionDetails From(
        string costCentreCode,
        Guid supplierId,
        string justification,
        DateOnly neededBy,
        IReadOnlyList<LineRequest> lines) =>
        new(
            costCentreCode,
            supplierId,
            justification,
            neededBy,
            [.. lines.Select(static line => new LineInput(line.Description, line.Quantity, line.UnitOfMeasure, line.UnitPrice))]);
}
