using MassTransit;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Suppliers.Application;
using Matchbook.Suppliers.Domain;
using Matchbook.Suppliers.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Infrastructure;

/// <remarks>
/// EF builds the model once per process and keeps it, so the IBAN converter holds the protector of the first
/// context created. There is one protector per process, built from configuration at startup, so that is the
/// only one it could be.
/// </remarks>
public sealed class SuppliersDbContext(DbContextOptions<SuppliersDbContext> options, ColumnProtector protector)
    : DbContext(options), ISuppliersDb
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new BankAccountConfiguration(protector));

        // The outbox and inbox tables live in this database, so an event is written in the same transaction as
        // the change that caused it.
        modelBuilder.AddTransactionalOutboxEntities();
    }
}
