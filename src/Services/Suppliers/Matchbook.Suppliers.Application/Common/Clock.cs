namespace Matchbook.Suppliers.Application.Common;

internal static class Clock
{
    /// <summary>
    /// Now, in UTC, cut to the microsecond Postgres stores. A response built from memory then says exactly what a
    /// later read of the row says, which is what lets a repeated create answer byte for byte as the first did.
    /// </summary>
    public static DateTimeOffset UtcNowToTheMicrosecond(this TimeProvider clock)
    {
        DateTimeOffset now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
