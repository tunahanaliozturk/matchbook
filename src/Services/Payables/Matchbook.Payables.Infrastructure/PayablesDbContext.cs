using MassTransit;
using Matchbook.Payables.Application;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.Payables.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Matchbook.Payables.Infrastructure;

public sealed class PayablesDbContext(DbContextOptions<PayablesDbContext> options) : DbContext(options), IPayablesDb
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<PaymentRun> PaymentRuns => Set<PaymentRun>();

    public DbSet<PaymentRunItem> PaymentRunItems => Set<PaymentRunItem>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new ReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentRunConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentRunItemConfiguration());

        modelBuilder.AddTransactionalOutboxEntities();
    }
}
