using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Payables.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Matchbook.Payables.Application;

/// <summary>The Payables database as the handlers see it. The only abstraction over persistence.</summary>
public interface IPayablesDb
{
    DbSet<Invoice> Invoices { get; }

    /// <summary>Local copies of purchase orders, one row per order from the first time anything mentions it.</summary>
    DbSet<PurchaseOrder> PurchaseOrders { get; }

    DbSet<Receipt> Receipts { get; }

    DbSet<Supplier> Suppliers { get; }

    DbSet<PaymentRun> PaymentRuns { get; }

    DbSet<PaymentRunItem> PaymentRunItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For the payment-run handlers, which pair a tracked change to the run with set-based updates of its items and
    /// invoices; those have to commit or roll back together.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
