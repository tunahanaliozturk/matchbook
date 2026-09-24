using Matchbook.Budgets.Domain;
using Microsoft.Extensions.Logging;

namespace Matchbook.Budgets.Application.IntegrationEvents;

// A refusal is a business outcome, not an error, but it is the first thing anyone asks about ("why was my
// requisition refused?"), so it gets one line with the numbers in it.
internal static partial class FundsLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Refused to reserve {Amount} for requisition {RequisitionId} on {CostCentreCode}/{FiscalYear}: {Reason}")]
    public static partial void ReservationRefused(
        this ILogger logger, decimal amount, Guid requisitionId, string costCentreCode, int fiscalYear, FundsRefusal reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Refused to commit {Amount} for purchase order {PurchaseOrderId}, attempt {Attempt}: {Reason}")]
    public static partial void CommitmentRefused(
        this ILogger logger, decimal amount, Guid purchaseOrderId, int attempt, FundsRefusal reason);

    // Allowed by design and reported, not refused, but someone owns that budget and should hear about it.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Invoice {InvoiceId} took budget {BudgetId} past its allotment; it is overspent by {Overspend}")]
    public static partial void BudgetOverspent(this ILogger logger, Guid invoiceId, Guid budgetId, decimal overspend);
}
