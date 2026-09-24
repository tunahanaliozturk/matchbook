namespace Matchbook.Payables.Infrastructure.Configurations;

internal static class Columns
{
    /// <summary>
    /// A shadow <c>uint</c> marked as the row version, which Npgsql maps to Postgres's own <c>xmin</c>: every update
    /// changes it, and an update that started from an older one fails instead of overwriting.
    /// </summary>
    public const string RowVersion = "RowVersion";

    /// <summary>Enums are stored by name, which keeps the partial index filters and ad hoc queries readable.</summary>
    public const int EnumLength = 40;
}
