using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application;

/// <summary>
/// The service's database as the handlers see it, implemented by the DbContext in Infrastructure. The only
/// abstraction over persistence: handlers query the set directly, and bank accounts are reached through their
/// supplier because nothing changes one without the other.
/// </summary>
public interface ISuppliersDb
{
    DbSet<Supplier> Suppliers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
