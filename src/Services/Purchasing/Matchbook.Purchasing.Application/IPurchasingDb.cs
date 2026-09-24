using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Purchasing.Application;

/// <summary>
/// Purchasing's database as the handlers see it: the sets they query and change, one unit of work, and the one
/// thing a DbSet cannot give them, the next order number.
/// </summary>
public interface IPurchasingDb
{
    DbSet<PurchaseOrder> PurchaseOrders { get; }

    DbSet<GoodsReceipt> GoodsReceipts { get; }

    DbSet<MatchedInvoice> MatchedInvoices { get; }

    DbSet<Supplier> Suppliers { get; }

    /// <summary>
    /// Takes the next value of the order number sequence. The value is spent even if the transaction rolls back,
    /// so numbers can have gaps; they are never reused.
    /// </summary>
    Task<long> NextPurchaseOrderSequenceAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
