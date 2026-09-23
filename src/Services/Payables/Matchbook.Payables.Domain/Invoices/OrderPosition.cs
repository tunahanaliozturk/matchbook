using Matchbook.Payables.Domain.Orders;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>
/// Per order line, how much has been received and how much matched invoices have already billed. A match reads it,
/// and each invoice that matches adds to it, so the next invoice on the same order sees what the last one took.
/// </summary>
public sealed class OrderPosition
{
    private readonly Dictionary<int, decimal> _received;
    private readonly Dictionary<int, decimal> _invoiced;

    public OrderPosition(IReadOnlyDictionary<int, decimal> received, IReadOnlyDictionary<int, decimal> invoiced)
    {
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(invoiced);

        _received = new Dictionary<int, decimal>(received);
        _invoiced = new Dictionary<int, decimal>(invoiced);
    }

    public static OrderPosition Empty() => new(new Dictionary<int, decimal>(), new Dictionary<int, decimal>());

    public decimal ReceivedOn(int lineNumber) => _received.GetValueOrDefault(lineNumber);

    public decimal InvoicedOn(int lineNumber) => _invoiced.GetValueOrDefault(lineNumber);

    public void RecordReceived(Receipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        foreach (ReceivedLine line in receipt.Lines)
        {
            _received[line.LineNumber] = ReceivedOn(line.LineNumber) + line.Quantity;
        }
    }

    public void RecordMatched(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        foreach (InvoiceLine line in invoice.Lines)
        {
            _invoiced[line.LineNumber] = InvoicedOn(line.LineNumber) + line.Quantity;
        }
    }
}
