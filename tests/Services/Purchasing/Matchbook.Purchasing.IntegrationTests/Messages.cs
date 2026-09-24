using Matchbook.Contracts.Budgets;
using Matchbook.Contracts.Payables;
using Matchbook.Contracts.Purchasing;
using Matchbook.Contracts.Requisitions;
using Matchbook.Contracts.Suppliers;
using Matchbook.SharedKernel;
using Matchbook.Testing;

namespace Matchbook.Purchasing.IntegrationTests;

/// <summary>The events the other services would send, shaped the way they send them.</summary>
internal static class Messages
{
    public static SupplierChanged Supplier(
        Guid supplierId, long version, string status, string legalName = "Acme Office Supplies GmbH") =>
        new(supplierId, version, legalName, "DE", status, 30, null, DateTimeOffset.UtcNow);

    public static RequisitionApproved Approval(Guid supplierId, params (decimal Quantity, decimal UnitPrice)[] lines)
    {
        RequisitionLine[] requisitionLines =
        [
            .. lines.Select(static (line, index) => new RequisitionLine(
                index + 1, $"Item {index + 1}", line.Quantity, "each", line.UnitPrice, Amounts.Line(line.Quantity, line.UnitPrice))),
        ];

        return new RequisitionApproved(
            Guid.CreateVersion7(),
            "REQ-2026-000042",
            TestUsers.Rita.Id,
            "ENG-PLATFORM",
            2026,
            supplierId,
            requisitionLines,
            requisitionLines.Sum(static line => line.Amount),
            [TestUsers.Mark.Id],
            DateTimeOffset.UtcNow);
    }

    public static FundsCommitted Committed(PurchaseOrderCommitmentRequested request) =>
        new(
            request.PurchaseOrderId,
            request.RequisitionId,
            request.Attempt,
            request.CostCentreCode,
            request.FiscalYear,
            request.Amount,
            DateTimeOffset.UtcNow);

    public static FundsCommitmentRejected Rejected(PurchaseOrderCommitmentRequested request, string reason) =>
        new(request.PurchaseOrderId, request.RequisitionId, request.Attempt, request.Amount, 0m, reason, DateTimeOffset.UtcNow);

    public static InvoiceMatched Invoice(Guid invoiceId, Guid orderId, Guid supplierId, params (int Line, decimal Quantity)[] lines)
    {
        MatchedLine[] matched = [.. lines.Select(static line => new MatchedLine(line.Line, line.Quantity, 1m, Amounts.Line(line.Quantity, 1m)))];
        return new InvoiceMatched(
            invoiceId, orderId, supplierId, "INV-1", matched, matched.Sum(static line => line.Amount), DateTimeOffset.UtcNow);
    }
}
