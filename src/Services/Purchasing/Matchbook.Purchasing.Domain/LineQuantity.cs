namespace Matchbook.Purchasing.Domain;

/// <summary>A quantity against one order line, as a receipt or a matched invoice states it.</summary>
public readonly record struct LineQuantity(int LineNumber, decimal Quantity);
