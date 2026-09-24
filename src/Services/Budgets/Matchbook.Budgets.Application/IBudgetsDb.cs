using Matchbook.Budgets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Matchbook.Budgets.Application;

/// <summary>
/// The service's database as the handlers see it. Implemented by the DbContext in Infrastructure, and the only
/// abstraction over persistence: handlers query the sets directly.
/// </summary>
public interface IBudgetsDb
{
    DbSet<CostCentre> CostCentres { get; }

    DbSet<Budget> Budgets { get; }

    DbSet<RequisitionReservation> Reservations { get; }

    DbSet<OrderCommitment> Commitments { get; }

    DbSet<LedgerEntry> Ledger { get; }

    DbSet<IdempotentRequest> Requests { get; }

    /// <summary>For the transaction a set-based balance update shares with the rows saved after it.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
