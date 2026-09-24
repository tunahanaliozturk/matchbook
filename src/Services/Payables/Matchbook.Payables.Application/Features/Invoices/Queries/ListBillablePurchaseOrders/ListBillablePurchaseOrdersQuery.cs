using Matchbook.Payables.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListBillablePurchaseOrders;

/// <summary>A page of the orders an invoice can still bill, newest first, optionally for one supplier.</summary>
public sealed record ListBillablePurchaseOrdersQuery(Guid? SupplierId, Guid? After, int Limit) : IQuery<Page<BillablePurchaseOrder>>;

/// <summary>An issued order from Payables' local copy, with its lines as ordered, which is what an invoice bills.</summary>
public sealed record BillablePurchaseOrder(
    Guid Id,
    string Number,
    Guid SupplierId,
    DateTimeOffset IssuedAt,
    IReadOnlyList<BillablePurchaseOrderLine> Lines);

public sealed record BillablePurchaseOrderLine(int LineNumber, decimal Quantity, decimal UnitPrice);
