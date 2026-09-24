using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchbook.Budgets.Infrastructure;

/// <summary>
/// For <c>dotnet ef</c> only. A migration is generated from the model, not from a live database, so the connection
/// string never has to reach anything and carries no credentials.
/// </summary>
public sealed class BudgetsDbContextFactory : IDesignTimeDbContextFactory<BudgetsDbContext>
{
    public BudgetsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BudgetsDbContext>()
            .UseNpgsql("Host=localhost;Database=matchbook_budgets")
            .UseSnakeCaseNamingConvention()
            .Options);
}
