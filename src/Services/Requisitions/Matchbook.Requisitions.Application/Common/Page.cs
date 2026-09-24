namespace Matchbook.Requisitions.Application.Common;

/// <summary>
/// One page of a keyset-paginated list. <see cref="NextCursor"/> is null on the last page; otherwise pass it
/// back as <c>after</c> to get the next one.
/// </summary>
public sealed record Page<T>(IReadOnlyList<T> Items, long? NextCursor);

public static class Page
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    internal static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    /// <summary>Builds a page from a query that asked for one row more than <paramref name="limit"/>.</summary>
    internal static Page<T> Of<T>(List<Keyed<T>> rows, int limit) =>
        new(
            [.. rows.Take(limit).Select(static row => row.Item)],
            rows.Count > limit ? rows[limit - 1].Serial : null);
}

internal sealed record Keyed<T>(long Serial, T Item);
