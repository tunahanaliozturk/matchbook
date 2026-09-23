namespace Matchbook.Purchasing.Domain;

/// <summary>The quantity of one order line in one receipt.</summary>
public sealed class GoodsReceiptLine
{
    internal GoodsReceiptLine(int lineNumber, decimal quantity)
    {
        LineNumber = lineNumber;
        Quantity = quantity;
    }

    public int LineNumber { get; private set; }

    public decimal Quantity { get; private set; }
}
