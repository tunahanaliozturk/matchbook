using MassTransit;
using Matchbook.Payables.Application;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.Payables.Infrastructure.Configurations;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

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

    /// <summary>
    /// Saves, and translates the unique violations of Payables' own tables. Two are business answers: a supplier's
    /// invoice number already taken, and an invoice already in an active payment run. The keys of the local copies
    /// collide when two writers create the same row at once (the first mention of an order, a supplier or a receipt),
    /// which is a concurrency conflict: a consumer retries it, and an API caller gets a 409. Violations anywhere else,
    /// the messaging tables included, pass through untouched.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    private static Exception? Translate(DbUpdateException exception)
    {
        if (exception is DbUpdateConcurrencyException
            || exception.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            return null;
        }

        return violation.ConstraintName switch
        {
            InvoiceConfiguration.UniqueNumberIndex => new BusinessRuleException(
                "invoice.duplicate",
                "The supplier already has an invoice with this number.",
                ViolationKind.Conflict),
            PaymentRunItemConfiguration.ActiveInvoiceIndex => new BusinessRuleException(
                "payment_run.invoice_taken",
                "Some of these invoices were taken by another payment run at the same moment. Draft again.",
                ViolationKind.Conflict),
            "pk_purchase_orders" or "pk_suppliers" or "pk_receipts" => new DbUpdateConcurrencyException(
                "Another change created the same record first.",
                exception),
            _ => null,
        };
    }

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
