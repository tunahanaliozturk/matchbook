using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchbook.Requisitions.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the host. Generating a migration never opens the
/// connection, so the connection string only has to parse.
/// </summary>
public sealed class RequisitionsDbContextFactory : IDesignTimeDbContextFactory<RequisitionsDbContext>
{
    public RequisitionsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<RequisitionsDbContext>()
            .UseNpgsql("Host=localhost;Database=requisitions;Username=requisitions")
            .UseSnakeCaseNamingConvention()
            .Options);
}
