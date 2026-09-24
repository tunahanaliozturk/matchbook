using MassTransit;
using Matchbook.Purchasing.Application;
using Matchbook.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Matchbook.Purchasing.Infrastructure.Persistence;

public sealed class PurchasingDbContext(DbContextOptions<PurchasingDbContext> options)
    : DbContext(options), IPurchasingDb
{
    // One sequence for every year rather than one per year: see docs/services/purchasing.md.
    private const string NumberSequence = "purchase_order_number_seq";

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();

    public DbSet<MatchedInvoice> MatchedInvoices => Set<MatchedInvoice>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public Task<long> NextPurchaseOrderSequenceAsync(CancellationToken cancellationToken) =>
        Database
            .SqlQueryRaw<long>("SELECT nextval('" + NumberSequence + "') AS \"Value\"")
            .SingleAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncludeOrderRowInEveryChange();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IncludeOrderRowInEveryChange();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(NumberSequence);
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new MatchedInvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.AddTransactionalOutboxEntities();
    }

    // Postgres checks xmin only on a row the UPDATE touches. Recording a receipt or an invoice changes the lines
    // and nothing on the order row, so without this two concurrent receipts would both pass the over-receipt
    // rule against the same totals and the second write would overwrite the first. Marking the order modified
    // puts its row, and its xmin check, into every save that changes anything inside it.
    private void IncludeOrderRowInEveryChange()
    {
        foreach (EntityEntry<PurchaseOrder> order in ChangeTracker.Entries<PurchaseOrder>())
        {
            if (order.State == EntityState.Unchanged
                && order.Entity.Lines.Any(line => Entry(line).State != EntityState.Unchanged))
            {
                order.State = EntityState.Modified;
            }
        }
    }
}
