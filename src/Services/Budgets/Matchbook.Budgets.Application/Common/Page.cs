namespace Matchbook.Budgets.Application.Common;

/// <summary>A page of a list. <see cref="Next"/> is the cursor for the following page, null on the last one.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? Next);

internal static class Paging
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static int Limit(int? requested) => Math.Clamp(requested ?? DefaultLimit, 1, MaxLimit);

    /// <summary>
    /// Queries read one row more than the page holds; that extra row only says there is a next page, so the last
    /// page never hands out a cursor that leads to an empty one.
    /// </summary>
    public static Page<T> Of<T>(List<T> rows, int limit, Func<T, string> cursor)
    {
        if (rows.Count <= limit)
        {
            return new Page<T>(rows, Next: null);
        }

        rows.RemoveAt(limit);
        return new Page<T>(rows, cursor(rows[^1]));
    }
}
