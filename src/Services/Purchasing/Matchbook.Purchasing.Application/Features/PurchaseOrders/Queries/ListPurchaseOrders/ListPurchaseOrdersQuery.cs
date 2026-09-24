using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.ListPurchaseOrders;

/// <summary>A page of orders, newest first, optionally of one status.</summary>
/// <param name="After">The <see cref="PurchaseOrderPage.Next"/> of the previous page; null for the first.</param>
public sealed record ListPurchaseOrdersQuery(PurchaseOrderStatus? Status, Guid? After, int Limit) : IQuery<PurchaseOrderPage>;

/// <param name="SupplierName">From the local copy of the supplier; null until its first snapshot arrives.</param>
public sealed record PurchaseOrderSummary(
    Guid Id,
    string Number,
    PurchaseOrderStatus Status,
    Guid SupplierId,
    string? SupplierName,
    string CostCentreCode,
    decimal Amount,
    DateTimeOffset DraftedAt);

/// <param name="Next">The cursor for the following page, or null when this is the last.</param>
public sealed record PurchaseOrderPage(IReadOnlyList<PurchaseOrderSummary> Items, Guid? Next);
