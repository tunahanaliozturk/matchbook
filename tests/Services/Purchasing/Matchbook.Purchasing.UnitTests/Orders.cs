using Matchbook.Purchasing.Domain;

namespace Matchbook.Purchasing.UnitTests;

/// <summary>Purchase orders in each state, built through the aggregate's own methods so no test fakes a state.</summary>
internal static class Orders
{
    public static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Line 1: 10 at 12.50. Line 2: 4 at 99.99. Amount 524.96.</summary>
    public static readonly (decimal Quantity, decimal UnitPrice)[] TwoLines = [(10m, 12.50m), (4m, 99.99m)];

    public static ApprovedRequisition Requisition(params (decimal Quantity, decimal UnitPrice)[] lines) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "ENG-PLATFORM",
            2026,
            [.. lines.Select((line, index) => new DraftLine(index + 1, $"Item {index + 1}", line.Quantity, "each", line.UnitPrice))]);

    public static PurchaseOrder Draft(params (decimal Quantity, decimal UnitPrice)[] lines) =>
        PurchaseOrder.Draft(Requisition(lines.Length == 0 ? TwoLines : lines), 17, Now);

    public static Supplier ActiveSupplierOf(PurchaseOrder order) => new(order.SupplierId, 1, isActive: true);

    /// <summary>Sent for commitment by Bruno, attempt 1.</summary>
    public static PurchaseOrder Pending(params (decimal Quantity, decimal UnitPrice)[] lines)
    {
        PurchaseOrder order = Draft(lines);
        order.RequestIssue(People.Bruno, ActiveSupplierOf(order));
        return order;
    }

    /// <summary>Issued by Bruno on attempt 1.</summary>
    public static PurchaseOrder Issued(params (decimal Quantity, decimal UnitPrice)[] lines)
    {
        PurchaseOrder order = Pending(lines);
        order.ConfirmCommitment(1, Now);
        return order;
    }

    public static GoodsReceipt Receive(this PurchaseOrder order, params (int Line, decimal Quantity)[] quantities) =>
        order.RecordReceipt(People.Rosa, Guid.CreateVersion7(), [.. quantities.Select(static q => new LineQuantity(q.Line, q.Quantity))], Now);

    public static bool Invoice(this PurchaseOrder order, params (int Line, decimal Quantity)[] quantities) =>
        order.RecordInvoice([.. quantities.Select(static q => new LineQuantity(q.Line, q.Quantity))], Now);

    public static OrderLine Line(this PurchaseOrder order, int lineNumber) =>
        order.Lines.Single(line => line.LineNumber == lineNumber);

    /// <summary>An order in the named state, for rules that hold across several states.</summary>
    public static PurchaseOrder InState(string state) => state switch
    {
        "draft" => Draft(),
        "pending" => Pending(),
        "issued" => Issued(),
        "short-closed" => ShortClosed(),
        "cancelled" => Cancelled(),
        "completed" => Completed(),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "No such state in the tests."),
    };

    private static PurchaseOrder ShortClosed()
    {
        PurchaseOrder order = Issued();
        order.Receive((1, 1m));
        order.ShortClose(People.Bruno, Now);
        return order;
    }

    private static PurchaseOrder Cancelled()
    {
        PurchaseOrder order = Draft();
        order.Cancel(People.Bruno, Now);
        return order;
    }

    private static PurchaseOrder Completed()
    {
        PurchaseOrder order = Issued();
        order.Receive((1, 10m), (2, 4m));
        order.Invoice((1, 10m), (2, 4m));
        return order;
    }
}
