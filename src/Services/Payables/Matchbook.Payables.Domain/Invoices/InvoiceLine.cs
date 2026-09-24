using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>One billed line: which purchase order line it bills, how many, and at what unit price.</summary>
public sealed record InvoiceLine(int LineNumber, decimal Quantity, decimal UnitPrice)
{
    public decimal Amount => Amounts.Line(Quantity, UnitPrice);
}
