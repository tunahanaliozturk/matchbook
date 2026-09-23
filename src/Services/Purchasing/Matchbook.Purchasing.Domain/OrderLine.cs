using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Domain;

/// <summary>
/// One line of a purchase order, with running totals of what was received and invoiced against it. The order
/// decides every change; the line only holds the numbers, so the rules live in one place.
/// </summary>
public sealed class OrderLine
{
    private OrderLine(int lineNumber, string description, string unitOfMeasure)
    {
        LineNumber = lineNumber;
        Description = description;
        UnitOfMeasure = unitOfMeasure;
    }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public string UnitOfMeasure { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    /// <summary><c>Amounts.Line(Quantity, UnitPrice)</c>, kept so totals and events never recompute it differently.</summary>
    public decimal Amount { get; private set; }

    public decimal ReceivedQuantity { get; private set; }

    public decimal InvoicedQuantity { get; private set; }

    /// <summary>What can still be received.</summary>
    public decimal OpenQuantity => Quantity - ReceivedQuantity;

    /// <summary>Received and invoiced in full. A line ordered at zero is settled from the start.</summary>
    public bool IsSettled => ReceivedQuantity == Quantity && InvoicedQuantity == Quantity;

    internal static OrderLine Create(DraftLine draft)
    {
        if (draft.LineNumber <= 0)
        {
            throw new BusinessRuleException(
                "purchase_order.invalid_line_number", "Line numbers start at one.", ViolationKind.Invalid);
        }

        if (string.IsNullOrWhiteSpace(draft.Description))
        {
            throw new BusinessRuleException(
                "purchase_order.description_required", "Every line needs a description.", ViolationKind.Invalid);
        }

        if (string.IsNullOrWhiteSpace(draft.UnitOfMeasure))
        {
            throw new BusinessRuleException(
                "purchase_order.unit_of_measure_required", "Every line needs a unit of measure.", ViolationKind.Invalid);
        }

        OrderLine line = new(draft.LineNumber, draft.Description, draft.UnitOfMeasure);
        line.Reprice(draft.Quantity, draft.UnitPrice, LineValues.LineAmount(draft.Quantity, draft.UnitPrice));
        return line;
    }

    internal void Reprice(decimal quantity, decimal unitPrice, decimal amount)
    {
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = amount;
    }

    internal void Receive(decimal quantity) => ReceivedQuantity += quantity;

    internal void Invoice(decimal quantity) => InvoicedQuantity += quantity;
}
