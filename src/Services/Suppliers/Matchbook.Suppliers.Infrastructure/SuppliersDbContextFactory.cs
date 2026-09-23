using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchbook.Suppliers.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the host. Adding a migration never connects, so the
/// connection string only has to name the provider's shape.
/// </summary>
public sealed class SuppliersDbContextFactory : IDesignTimeDbContextFactory<SuppliersDbContext>
{
    public SuppliersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SuppliersDbContext>()
            .UseNpgsql("Host=localhost;Database=suppliers;Username=suppliers")
            .UseSnakeCaseNamingConvention()
            .Options);
}
