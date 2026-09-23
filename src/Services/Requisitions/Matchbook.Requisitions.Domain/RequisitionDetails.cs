namespace Matchbook.Requisitions.Domain;

/// <summary>What a requester fills in, whether drafting a requisition or editing one.</summary>
public sealed record RequisitionDetails(
    string CostCentreCode,
    Guid SupplierId,
    string Justification,
    DateOnly NeededBy,
    IReadOnlyList<LineInput> Lines);

/// <summary>One line as the requester typed it. The amount is always computed, never taken from input.</summary>
public sealed record LineInput(string Description, decimal Quantity, string UnitOfMeasure, decimal UnitPrice);
