using System.Globalization;
using Matchbook.Payables.Domain.Orders;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>
/// A supplier invoice, from capture through the three-way match to payment. Payment itself is recorded by the
/// payment run that pays it.
/// </summary>
public sealed class Invoice
{
    public const int MaxLines = 50;

    public const int MaxReasonLength = 500;

    // numeric(18,2) holds sixteen integer digits; anything larger would fail in the database instead of here.
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;
    private const decimal MaxQuantity = 999_999_999.999m;
    private const decimal MaxUnitPrice = 999_999_999.9999m;

    private readonly List<InvoiceLine> _lines = [];

    private Invoice(
        Guid id,
        Guid supplierId,
        string number,
        string normalisedNumber,
        DateOnly invoiceDate,
        Guid purchaseOrderId,
        decimal total,
        InvoiceStatus status,
        Guid capturedBy,
        DateTimeOffset capturedAt)
    {
        Id = id;
        SupplierId = supplierId;
        Number = number;
        NormalisedNumber = normalisedNumber;
        InvoiceDate = invoiceDate;
        PurchaseOrderId = purchaseOrderId;
        Total = total;
        Status = status;
        CapturedBy = capturedBy;
        CapturedAt = capturedAt;
    }

    public Guid Id { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>The supplier's invoice number as written.</summary>
    public string Number { get; private set; }

    /// <summary>The number as <see cref="InvoiceNumber.Normalise"/> has it; unique per supplier.</summary>
    public string NormalisedNumber { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public decimal Total { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public InvoiceStatus Status { get; private set; }

    /// <summary>Why the last match decided what it did. Null until the invoice has been matched once.</summary>
    public MatchReason? Reason { get; private set; }

    public string? ReasonDetail { get; private set; }

    public Guid CapturedBy { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public Guid? SuspectedDuplicateOf { get; private set; }

    public Guid? DuplicateClearedBy { get; private set; }

    public DateTimeOffset? DuplicateClearedAt { get; private set; }

    public Guid? VarianceAcceptedBy { get; private set; }

    public DateTimeOffset? VarianceAcceptedAt { get; private set; }

    public string? VarianceAcceptanceReason { get; private set; }

    public DateTimeOffset? MatchedAt { get; private set; }

    /// <summary>
    /// Invoice date plus the supplier's payment terms as Payables knew them at the match. Null when the supplier had
    /// not arrived by then; a payment run then uses the terms it finds.
    /// </summary>
    public DateOnly? DueDate { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public bool IsVarianceAccepted => VarianceAcceptedBy is not null;

    public static Invoice Capture(
        Guid id,
        Guid supplierId,
        string number,
        DateOnly invoiceDate,
        Guid purchaseOrderId,
        IReadOnlyCollection<InvoiceLine> lines,
        decimal declaredTotal,
        Actor clerk,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(clerk);

        if (supplierId == Guid.Empty)
        {
            throw Invalid("invoice.supplier_required", "The invoice needs a supplier.");
        }

        if (purchaseOrderId == Guid.Empty)
        {
            throw Invalid("invoice.purchase_order_required", "The invoice needs the purchase order it bills.");
        }

        InvoiceNumber invoiceNumber = InvoiceNumber.Parse(number);
        ValidateLines(lines);
        ValidateTotal(lines, declaredTotal);

        var invoice = new Invoice(
            id,
            supplierId,
            invoiceNumber.Value,
            invoiceNumber.Normalised,
            invoiceDate,
            purchaseOrderId,
            declaredTotal,
            InvoiceStatus.Captured,
            clerk.Id,
            now);
        invoice._lines.AddRange(lines.OrderBy(line => line.LineNumber));
        return invoice;
    }

    public void HoldAsSuspectedDuplicate(Guid otherInvoiceId)
    {
        if (Status != InvoiceStatus.Captured)
        {
            throw new InvalidOperationException("Only a freshly captured invoice can be held as a suspected duplicate.");
        }

        Status = InvoiceStatus.SuspectedDuplicate;
        SuspectedDuplicateOf = otherInvoiceId;
    }

    /// <summary>An approver confirms the invoice is not a duplicate. It goes on to the match.</summary>
    public void ClearSuspectedDuplicate(Actor approver, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approver);

        EnsureSecondApprover(approver);
        if (Status != InvoiceStatus.SuspectedDuplicate)
        {
            throw new BusinessRuleException(
                "invoice.not_suspected_duplicate",
                "The invoice is not held as a suspected duplicate.",
                ViolationKind.Conflict);
        }

        DuplicateClearedBy = approver.Id;
        DuplicateClearedAt = now;
        Status = InvoiceStatus.Captured;
    }

    /// <summary>
    /// An approver accepts a price outside tolerance. The invoice is matched again straight after, and may still
    /// wait for goods if another invoice took them in the meantime; the acceptance stands.
    /// </summary>
    public void AcceptPriceVariance(Actor approver, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approver);

        EnsureSecondApprover(approver);
        if (Status != InvoiceStatus.PriceVariance)
        {
            throw new BusinessRuleException(
                "invoice.no_price_variance",
                "The invoice is not waiting on a price variance.",
                ViolationKind.Conflict);
        }

        string trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxReasonLength)
        {
            throw Invalid(
                "invoice.reason_required",
                $"Accepting a price variance needs a reason of at most {MaxReasonLength} characters.");
        }

        VarianceAcceptedBy = approver.Id;
        VarianceAcceptedAt = now;
        VarianceAcceptanceReason = trimmed;
    }

    /// <summary>
    /// Runs the three-way match and moves the invoice to what it decided. A match makes the invoice payable, due on
    /// the invoice date plus <paramref name="paymentTermsDays"/> when the supplier's terms are known.
    /// </summary>
    public MatchResult Evaluate(PurchaseOrder? order, OrderPosition position, int? paymentTermsDays, DateTimeOffset now)
    {
        if (Status is not (InvoiceStatus.Captured or InvoiceStatus.AwaitingPurchaseOrder
            or InvoiceStatus.AwaitingReceipt or InvoiceStatus.PriceVariance))
        {
            throw new InvalidOperationException($"An invoice in {Status} is not open to a match.");
        }

        MatchResult result = ThreeWayMatch.Evaluate(this, order, position);

        Status = result.Outcome switch
        {
            MatchOutcome.Matched => InvoiceStatus.Payable,
            MatchOutcome.AwaitingPurchaseOrder => InvoiceStatus.AwaitingPurchaseOrder,
            MatchOutcome.AwaitingReceipt => InvoiceStatus.AwaitingReceipt,
            MatchOutcome.PriceVariance => InvoiceStatus.PriceVariance,
            MatchOutcome.Rejected => InvoiceStatus.Rejected,
            _ => throw new InvalidOperationException($"Unknown match outcome {result.Outcome}."),
        };
        Reason = result.Reason;
        ReasonDetail = result.Detail;

        if (result.Outcome == MatchOutcome.Matched)
        {
            MatchedAt = now;
            DueDate = paymentTermsDays is { } days ? InvoiceDate.AddDays(days) : null;
        }

        return result;
    }

    private void EnsureSecondApprover(Actor approver)
    {
        if (!approver.IsIn(Roles.ApApprover))
        {
            throw new BusinessRuleException(
                "invoice.approver_required",
                "Only an AP approver may do this.",
                ViolationKind.Forbidden);
        }

        if (approver.Id == CapturedBy)
        {
            throw new BusinessRuleException(
                "invoice.self_approval",
                "The person who captured an invoice cannot approve it.",
                ViolationKind.Forbidden);
        }
    }

    private static void ValidateLines(IReadOnlyCollection<InvoiceLine> lines)
    {
        if (lines.Count == 0)
        {
            throw Invalid("invoice.lines_required", "The invoice needs at least one line.");
        }

        if (lines.Count > MaxLines)
        {
            throw Invalid("invoice.too_many_lines", $"An invoice has at most {MaxLines} lines.");
        }

        var seen = new HashSet<int>();
        foreach (InvoiceLine line in lines)
        {
            if (line.LineNumber < 1)
            {
                throw Invalid("invoice.line_number_invalid", "Line numbers start at 1.");
            }

            if (!seen.Add(line.LineNumber))
            {
                throw Invalid("invoice.duplicate_line", $"Order line {line.LineNumber} is billed twice.");
            }

            if (line.Quantity <= 0 || line.Quantity > MaxQuantity || !Amounts.FitsScale(line.Quantity, Amounts.QuantityScale))
            {
                throw Invalid(
                    "invoice.quantity_invalid",
                    $"Line {line.LineNumber}: the quantity must be above zero with at most three decimals.");
            }

            if (line.UnitPrice < 0 || line.UnitPrice > MaxUnitPrice || !Amounts.FitsScale(line.UnitPrice, Amounts.UnitPriceScale))
            {
                throw Invalid(
                    "invoice.unit_price_invalid",
                    $"Line {line.LineNumber}: the unit price cannot be negative and has at most four decimals.");
            }
        }
    }

    private static void ValidateTotal(IReadOnlyCollection<InvoiceLine> lines, decimal declaredTotal)
    {
        if (declaredTotal <= 0 || declaredTotal > MaxAmount || !Amounts.FitsScale(declaredTotal, Amounts.AmountScale))
        {
            throw Invalid("invoice.total_invalid", "The total must be above zero with at most two decimals.");
        }

        decimal sum = lines.Sum(line => line.Amount);
        if (sum != declaredTotal)
        {
            throw Invalid(
                "invoice.total_mismatch",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The declared total {declaredTotal:0.00} is not the sum of the lines, {sum:0.00}."));
        }
    }

    private static BusinessRuleException Invalid(string code, string message) =>
        new(code, message, ViolationKind.Invalid);
}
