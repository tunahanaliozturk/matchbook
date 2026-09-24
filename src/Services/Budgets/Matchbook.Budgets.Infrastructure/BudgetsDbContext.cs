using MassTransit;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Domain;
using Matchbook.Budgets.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Budgets.Infrastructure;

public sealed class BudgetsDbContext(DbContextOptions<BudgetsDbContext> options) : DbContext(options), IBudgetsDb
{
    public DbSet<CostCentre> CostCentres => Set<CostCentre>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<RequisitionReservation> Reservations => Set<RequisitionReservation>();

    public DbSet<OrderCommitment> Commitments => Set<OrderCommitment>();

    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();

    public DbSet<IdempotentRequest> Requests => Set<IdempotentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ApplyConfiguration(new CostCentreConfiguration())
            .ApplyConfiguration(new BudgetConfiguration())
            .ApplyConfiguration(new RequisitionReservationConfiguration())
            .ApplyConfiguration(new OrderCommitmentConfiguration())
            .ApplyConfiguration(new LedgerEntryConfiguration())
            .ApplyConfiguration(new IdempotentRequestConfiguration());

        // The outbox and inbox live in this database so an event and the change that caused it commit together.
        modelBuilder.AddTransactionalOutboxEntities();
    }
}
