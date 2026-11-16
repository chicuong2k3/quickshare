namespace QuickShare.Querying;

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source, int page, int pageSize, CancellationToken ct = default)
    {
        var total = await source.CountAsync(ct);
        var items = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> source, int page, int pageSize, Func<TSource, TResult> selector, CancellationToken ct = default)
    {
        var total = await source.CountAsync(ct);
        var items = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<TResult>(items.Select(selector).ToList(), total, page, pageSize);
    }

    /// <summary>
    /// Newest-first keyset page. Fetches one row beyond <paramref name="limit"/> to decide whether
    /// a next cursor exists, so an exhausted list costs no extra round trip.
    /// </summary>
    /// <remarks>
    /// ponytail: keyed on CreatedAt alone. Two rows sharing the same 100ns tick would straddle the
    /// page boundary and one could be skipped — upgrade to a composite (CreatedAt, Id) cursor if
    /// rows ever get bulk-inserted with an identical timestamp.
    /// </remarks>
    public static async Task<CursorPage<TResult>> ToCursorPageAsync<TSource, TResult>(
        this IQueryable<TSource> source, string? cursor, int limit, Func<TSource, TResult> selector,
        CancellationToken ct = default)
        where TSource : ICursorKeyed
    {
        if (TryParseCursor(cursor, out var after))
        {
            source = source.Where(x => x.CreatedAt < after);
        }

        var rows = await source
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var next = hasMore && rows.Count > 0 ? FormatCursor(rows[^1].CreatedAt) : null;
        return new CursorPage<TResult>(rows.Select(selector).ToList(), next);
    }

    /// <summary>Cursors travel through query strings, so they are round-trip safe invariant ticks.</summary>
    public static string FormatCursor(DateTime createdAt) =>
        createdAt.Ticks.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseCursor(string? cursor, out DateTime createdAt)
    {
        createdAt = default;
        if (string.IsNullOrEmpty(cursor)
            || !long.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            || ticks < 0
            || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        createdAt = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }
}
