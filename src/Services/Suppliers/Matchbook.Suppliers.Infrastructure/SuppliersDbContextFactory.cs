using Matchbook.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchbook.Suppliers.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the host. Adding a migration never connects and never
/// encrypts anything, so the connection string only names the provider's shape and the key is all zeros.
/// </summary>
public sealed class SuppliersDbContextFactory : IDesignTimeDbContextFactory<SuppliersDbContext>
{
    public SuppliersDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<SuppliersDbContext>()
                .UseNpgsql("Host=localhost;Database=suppliers;Username=suppliers")
                .UseSnakeCaseNamingConvention()
                .Options,
            new ColumnProtector(new Dictionary<string, byte[]> { ["design"] = new byte[32] }, "design"));
}
