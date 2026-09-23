using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchbook.Purchasing.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the host. Adding a migration never connects, so the
/// connection string only has to parse. The naming convention must match the one the host registers, or the
/// migrations would describe different tables from the ones the service queries.
/// </summary>
public sealed class PurchasingDbContextFactory : IDesignTimeDbContextFactory<PurchasingDbContext>
{
    public PurchasingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseNpgsql("Host=localhost;Database=purchasing;Username=purchasing")
            .UseSnakeCaseNamingConvention()
            .Options);
}
