using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchbook.Payables.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the host. It never connects, so the
/// connection string only has to name the provider.
/// </summary>
internal sealed class PayablesDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PayablesDbContext>
{
    public PayablesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PayablesDbContext>()
            .UseNpgsql("Host=localhost;Database=payables")
            .UseSnakeCaseNamingConvention()
            .Options);
}
