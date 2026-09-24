using MassTransit;
using Matchbook.Requisitions.Application;
using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Infrastructure;

public sealed class RequisitionsDbContext(DbContextOptions<RequisitionsDbContext> options)
    : DbContext(options), IRequisitionsDb
{
    // One sequence for every year, with the year written into the number. A sequence per year would need DDL
    // at runtime, on the first requisition of each January, and would make the value useless as a sort key.
    internal const string SerialSequence = "requisition_serial";

    public DbSet<Requisition> Requisitions => Set<Requisition>();

    public DbSet<CostCentre> CostCentres => Set<CostCentre>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public Task<long> NextRequisitionSerialAsync(CancellationToken cancellationToken) =>
        Database
            .SqlQueryRaw<long>($"SELECT nextval('{SerialSequence}') AS \"Value\"")
            .SingleAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(SerialSequence);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RequisitionsDbContext).Assembly);
        modelBuilder.AddTransactionalOutboxEntities();
    }
}
