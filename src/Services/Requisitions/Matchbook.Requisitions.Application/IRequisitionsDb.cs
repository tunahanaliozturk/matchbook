using Matchbook.Requisitions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application;

/// <summary>
/// The service's database as the handlers see it, implemented by the DbContext. The only abstraction over
/// persistence: handlers query the sets directly, and there are no repositories.
/// </summary>
public interface IRequisitionsDb
{
    DbSet<Requisition> Requisitions { get; }

    DbSet<CostCentre> CostCentres { get; }

    DbSet<Supplier> Suppliers { get; }

    /// <summary>The next value of the sequence behind requisition numbers. Never reused, even on rollback.</summary>
    Task<long> NextRequisitionSerialAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
