namespace Matchbook.Purchasing.Domain;

/// <summary>A requisition line as it becomes an order line. The amount is always recomputed, never copied.</summary>
public sealed record DraftLine(
    int LineNumber,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice);
