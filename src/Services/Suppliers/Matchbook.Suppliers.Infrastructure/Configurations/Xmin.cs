namespace Matchbook.Suppliers.Infrastructure.Configurations;

/// <summary>
/// Postgres's own row version: a system column every row has, which changes on every update. Mapped as a shadow
/// property so the domain carries no persistence field, and never created by a migration.
/// </summary>
internal static class Xmin
{
    public const string Column = "xmin";
}
