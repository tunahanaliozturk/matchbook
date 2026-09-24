namespace Matchbook.Payables.Application.Common;

/// <summary>One page of a keyset-paginated list. Pass <see cref="Next"/> as <c>after</c> to get the page that follows; null means this was the last.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, Guid? Next);

internal static class Paging
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public static int Clamp(int limit) => limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    /// <summary>Turns <paramref name="rows"/>, fetched with one row more than the limit, into a page.</summary>
    public static Page<T> ToPage<T>(List<T> rows, int limit, Func<T, Guid> key)
    {
        if (rows.Count <= limit)
        {
            return new Page<T>(rows, null);
        }

        rows.RemoveAt(limit);
        return new Page<T>(rows, key(rows[^1]));
    }
}
