using System.Globalization;
using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Domain;

/// <summary>
/// A purchase order, from the draft an approved requisition creates to its close. Every rule about editing,
/// issuing, receiving, invoicing and closing is decided here, against the running totals on its lines.
/// </summary>
public sealed class PurchaseOrder
{
    private readonly List<OrderLine> _lines = [];

    private PurchaseOrder(
        Guid id,
        string number,
        Guid requisitionId,
        Guid supplierId,
        string costCentreCode,
        int fiscalYear,
        DateTimeOffset draftedAt)
    {
        Id = id;
        Number = number;
        RequisitionId = requisitionId;
        SupplierId = supplierId;
        CostCentreCode = costCentreCode;
        FiscalYear = fiscalYear;
        DraftedAt = draftedAt;
    }

    public Guid Id { get; private set; }

    /// <summary><c>PO-&lt;year drafted&gt;-&lt;sequence, at least six digits&gt;</c>.</summary>
    public string Number { get; private set; }

    public Guid RequisitionId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string CostCentreCode { get; private set; }

    public int FiscalYear { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>The sum of the line amounts.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// The number of the last commitment request sent to Budgets, zero before the first. A reply is acted on only
    /// when it carries this number and the order is still waiting, which is what makes a stale or repeated reply
    /// harmless.
    /// </summary>
    public int CommitmentAttempt { get; private set; }

    /// <summary>Why Budgets refused the last commitment request, until the order is sent again.</summary>
    public string? CommitmentRejectionReason { get; private set; }

    /// <summary>
    /// The buyer who sent the order for commitment, and so the buyer who issued it once funds were committed.
    /// Cleared when Budgets refuses, because the next buyer to issue it is the one who counts.
    /// </summary>
    public Guid? IssuedBy { get; private set; }

    public DateTimeOffset DraftedAt { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>The buyer who short-closed or cancelled it. Empty when it completed on its own.</summary>
    public Guid? ClosedBy { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public bool IsClosed => Status is PurchaseOrderStatus.Completed
        or PurchaseOrderStatus.ShortClosed
        or PurchaseOrderStatus.Cancelled;

    /// <summary>Drafts the order for an approved requisition, numbered from <paramref name="sequence"/>.</summary>
    public static PurchaseOrder Draft(ApprovedRequisition requisition, long sequence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(requisition);
        ArgumentException.ThrowIfNullOrWhiteSpace(requisition.CostCentreCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);

        if (requisition.Lines.Count == 0)
        {
            throw new BusinessRuleException(
                "purchase_order.no_lines", "A purchase order needs at least one line.", ViolationKind.Invalid);
        }

        if (requisition.Lines.DistinctBy(static line => line.LineNumber).Count() != requisition.Lines.Count)
        {
            throw new BusinessRuleException(
                "purchase_order.duplicate_line", "Each line number may appear once.", ViolationKind.Invalid);
        }

        string number = string.Create(CultureInfo.InvariantCulture, $"PO-{now.UtcDateTime.Year}-{sequence:D6}");
        PurchaseOrder order = new(
            Guid.CreateVersion7(now),
            number,
            requisition.RequisitionId,
            requisition.SupplierId,
            requisition.CostCentreCode,
            requisition.FiscalYear,
            now);

        order._lines.AddRange(requisition.Lines.Select(OrderLine.Create));
        order.Amount = EnsureOrderAmount(order._lines.Sum(static line => line.Amount));
        return order;
    }

    /// <summary>
    /// Changes one line's quantity and unit price. Zero is allowed and keeps the line, so a buyer can drop a line
    /// from this order and still bring it back before issuing.
    /// </summary>
    public void AmendLine(Actor buyer, int lineNumber, decimal quantity, decimal unitPrice)
    {
        RequireRole(buyer, Roles.Buyer, "purchase_order.not_a_buyer");
        RequireStatus(PurchaseOrderStatus.Draft, "purchase_order.not_draft", "Only a draft can be changed.");

        OrderLine line = FindLine(lineNumber);
        decimal amount = LineValues.LineAmount(quantity, unitPrice);
        decimal total = EnsureOrderAmount(Amount - line.Amount + amount);

        line.Reprice(quantity, unitPrice, amount);
        Amount = total;
    }

    /// <summary>
    /// Sends the order to Budgets for commitment. The caller publishes the request with the new
    /// <see cref="CommitmentAttempt"/>.
    /// </summary>
    /// <param name="buyer">The buyer issuing it.</param>
    /// <param name="supplier">Purchasing's copy of the supplier, or null when it has never heard of it.</param>
    public void RequestIssue(Actor buyer, Supplier? supplier)
    {
        RequireRole(buyer, Roles.Buyer, "purchase_order.not_a_buyer");
        RequireStatus(PurchaseOrderStatus.Draft, "purchase_order.not_draft", "Only a draft can be issued.");

        if (supplier is not null && supplier.Id != SupplierId)
        {
            throw new ArgumentException("The supplier given is not this order's supplier.", nameof(supplier));
        }

        if (supplier is not { IsActive: true })
        {
            throw new BusinessRuleException(
                "purchase_order.supplier_not_active", "The supplier is not active, so it cannot be ordered from.");
        }

        if (_lines.TrueForAll(static line => line.Quantity == 0))
        {
            throw new BusinessRuleException(
                "purchase_order.nothing_ordered", "Every line is at zero, so there is nothing to order.");
        }

        Status = PurchaseOrderStatus.CommitmentPending;
        CommitmentAttempt++;
        CommitmentRejectionReason = null;
        IssuedBy = buyer.Id;
    }

    /// <summary>
    /// Applies Budgets' confirmation that funds are committed. Returns false, and changes nothing, for a reply to
    /// any other attempt or for an order no longer waiting: a stale reply, a redelivered one, or one that arrived
    /// after the order was cancelled.
    /// </summary>
    public bool ConfirmCommitment(int attempt, DateTimeOffset now)
    {
        if (!IsAwaiting(attempt))
        {
            return false;
        }

        Status = PurchaseOrderStatus.Issued;
        IssuedAt = now;
        return true;
    }

    /// <summary>Applies Budgets' refusal. The same attempt rule as <see cref="ConfirmCommitment"/> applies.</summary>
    public bool RejectCommitment(int attempt, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!IsAwaiting(attempt))
        {
            return false;
        }

        Status = PurchaseOrderStatus.Draft;
        CommitmentRejectionReason = reason;
        IssuedBy = null;
        return true;
    }

    /// <summary>
    /// Records goods received against the issued order. Quantities for the same line are added together. Either
    /// every line of the receipt is taken or none is.
    /// </summary>
    public GoodsReceipt RecordReceipt(Actor receiver, IReadOnlyList<LineQuantity> quantities, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        RequireRole(receiver, Roles.Receiver, "purchase_order.not_a_receiver");
        RequireStatus(PurchaseOrderStatus.Issued, "purchase_order.not_issued", "Goods are received only on an issued order.");

        if (receiver.Id == IssuedBy)
        {
            throw new BusinessRuleException(
                "purchase_order.receiver_is_buyer",
                "The buyer who issued an order may not also receive against it.",
                ViolationKind.Forbidden);
        }

        if (quantities.Count == 0)
        {
            throw new BusinessRuleException(
                "purchase_order.empty_receipt", "A receipt needs at least one line.", ViolationKind.Invalid);
        }

        if (quantities.Any(static q => q.Quantity <= 0))
        {
            throw new BusinessRuleException(
                "purchase_order.receipt_quantity_not_positive",
                "Every quantity on a receipt must be above zero.",
                ViolationKind.Invalid);
        }

        List<(OrderLine Line, decimal Quantity)> received = Resolve(quantities);

        foreach ((OrderLine line, decimal quantity) in received)
        {
            if (quantity > line.OpenQuantity)
            {
                throw new BusinessRuleException(
                    "purchase_order.over_receipt",
                    $"Line {line.LineNumber} has {line.OpenQuantity} left to receive, not {quantity}.");
            }
        }

        foreach ((OrderLine line, decimal quantity) in received)
        {
            line.Receive(quantity);
        }

        return GoodsReceipt.Record(Id, receiver.Id, received.Select(static r => new LineQuantity(r.Line.LineNumber, r.Quantity)), now);
    }

    /// <summary>
    /// Counts a matched invoice's quantities as invoiced, and completes the order when that settles every line.
    /// Returns true when it completed the order.
    /// </summary>
    /// <remarks>
    /// Payables never matches more than was received, and a receipt always reaches Purchasing's database before
    /// Payables can hear of it, so an invoice that would take a line past its receipts is a defect somewhere, not
    /// a race. It is refused rather than clamped: the message fails and is parked, where someone will look, and
    /// the order keeps totals that are true.
    /// </remarks>
    public bool RecordInvoice(IReadOnlyList<LineQuantity> quantities, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quantities);

        List<(OrderLine Line, decimal Quantity)> invoiced = Resolve(quantities);

        foreach ((OrderLine line, decimal quantity) in invoiced)
        {
            if (line.InvoicedQuantity + quantity > line.ReceivedQuantity)
            {
                throw new BusinessRuleException(
                    "purchase_order.invoiced_exceeds_received",
                    $"Line {line.LineNumber} would have more invoiced than the {line.ReceivedQuantity} received.");
            }
        }

        foreach ((OrderLine line, decimal quantity) in invoiced)
        {
            line.Invoice(quantity);
        }

        // A short-closed order keeps counting invoices for what it did receive, but it is already closed and
        // stays closed as it was.
        if (Status != PurchaseOrderStatus.Issued || !_lines.TrueForAll(static line => line.IsSettled))
        {
            return false;
        }

        Status = PurchaseOrderStatus.Completed;
        ClosedAt = now;
        return true;
    }

    /// <summary>
    /// Closes an issued order early. Nothing more will be received against it; invoices for what was received
    /// are still counted.
    /// </summary>
    /// <remarks>
    /// An issued order always has something open: the invoice that settles its last line completes it on the
    /// spot. That includes an order received in full and invoiced in part, which is exactly the one a buyer
    /// needs to close when the supplier will never bill the rest, or Budgets would hold that commitment forever.
    /// </remarks>
    public void ShortClose(Actor buyer, DateTimeOffset now)
    {
        RequireRole(buyer, Roles.Buyer, "purchase_order.not_a_buyer");
        RequireStatus(PurchaseOrderStatus.Issued, "purchase_order.not_issued", "Only an issued order can be short-closed.");

        Close(PurchaseOrderStatus.ShortClosed, buyer, now);
    }

    /// <summary>
    /// Cancels an order that is a draft, waiting for commitment, or issued with nothing received. Cancelling
    /// while waiting is allowed: the reply that comes back later finds a cancelled order and is ignored.
    /// </summary>
    public void Cancel(Actor buyer, DateTimeOffset now)
    {
        RequireRole(buyer, Roles.Buyer, "purchase_order.not_a_buyer");

        if (IsClosed)
        {
            throw new BusinessRuleException("purchase_order.closed", $"The order is already {Status}.");
        }

        if (_lines.Exists(static line => line.ReceivedQuantity > 0))
        {
            throw new BusinessRuleException(
                "purchase_order.goods_received",
                "Goods were received against this order, so it can be short-closed but not cancelled.");
        }

        Close(PurchaseOrderStatus.Cancelled, buyer, now);
    }

    private bool IsAwaiting(int attempt) =>
        Status == PurchaseOrderStatus.CommitmentPending && attempt == CommitmentAttempt;

    private void Close(PurchaseOrderStatus status, Actor buyer, DateTimeOffset now)
    {
        Status = status;
        ClosedBy = buyer.Id;
        ClosedAt = now;
    }

    private OrderLine FindLine(int lineNumber) =>
        _lines.Find(line => line.LineNumber == lineNumber)
        ?? throw new BusinessRuleException(
            "purchase_order.unknown_line", $"The order has no line {lineNumber}.", ViolationKind.Invalid);

    // Each quantity is checked on its own, then repeats of a line are added together, so the rules that follow
    // compare one total per line with that line's running totals.
    private List<(OrderLine Line, decimal Quantity)> Resolve(IReadOnlyList<LineQuantity> quantities)
    {
        foreach (LineQuantity quantity in quantities)
        {
            LineValues.EnsureQuantity(quantity.Quantity);
        }

        return
        [
            .. quantities
                .GroupBy(static q => q.LineNumber)
                .Select(group => (FindLine(group.Key), group.Sum(static q => q.Quantity))),
        ];
    }

    private void RequireStatus(PurchaseOrderStatus expected, string code, string message)
    {
        if (Status != expected)
        {
            throw new BusinessRuleException(code, $"{message} This order is {Status}.");
        }
    }

    // Roles are also checked by the API's authorization policies. Checking them here too means a handler wired
    // to the wrong policy fails closed instead of letting, say, an auditor issue an order.
    private static void RequireRole(Actor actor, string role, string code)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsIn(role))
        {
            throw new BusinessRuleException(code, $"Only a {role} may do this.", ViolationKind.Forbidden);
        }
    }

    private static decimal EnsureOrderAmount(decimal amount) =>
        amount <= LineValues.MaxAmount ? amount : throw LineValues.AmountTooLarge();
}
