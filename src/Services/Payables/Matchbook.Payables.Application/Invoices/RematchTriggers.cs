namespace Matchbook.Payables.Application.Invoices;

/// <summary>What arrived late and sent waiting invoices back to the match. Metric tag values.</summary>
public static class RematchTriggers
{
    public const string OrderIssued = "order_issued";
    public const string GoodsReceived = "goods_received";
    public const string OrderClosed = "order_closed";
}
