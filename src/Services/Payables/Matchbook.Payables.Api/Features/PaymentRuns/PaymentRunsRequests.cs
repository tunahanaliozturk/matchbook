using System.ComponentModel.DataAnnotations;

namespace Matchbook.Payables.Api.Features.PaymentRuns;

/// <summary>A payment run to draft for an execution date.</summary>
/// <param name="Id">Optional. Repeating a draft with the same id returns the run it created instead of a second one.</param>
public sealed record DraftPaymentRunRequest(Guid? Id, [Required] DateOnly? ExecutionDate);
