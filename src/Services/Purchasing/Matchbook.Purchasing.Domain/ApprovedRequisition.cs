namespace Matchbook.Purchasing.Domain;

/// <summary>What Purchasing needs from an approved requisition to draft an order for it.</summary>
public sealed record ApprovedRequisition(
    Guid RequisitionId,
    Guid SupplierId,
    string CostCentreCode,
    int FiscalYear,
    IReadOnlyList<DraftLine> Lines);
