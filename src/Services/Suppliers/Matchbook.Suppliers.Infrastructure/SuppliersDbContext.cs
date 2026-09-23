using MassTransit;
using Matchbook.Suppliers.Application;
using Matchbook.Suppliers.Domain;
using Matchbook.Suppliers.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Infrastructure;

public sealed class SuppliersDbContext(DbContextOptions<SuppliersDbContext> options) : DbContext(options), ISuppliersDb
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new BankAccountConfiguration());

        // The outbox and inbox tables live in this database, so an event is written in the same transaction as
        // the change that caused it.
        modelBuilder.AddTransactionalOutboxEntities();
    }
}
