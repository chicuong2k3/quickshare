namespace QuickShare.Querying;

/// <summary>
/// A keyset ("cursor") page. Unlike <see cref="PagedResult{T}"/> there is no total count and no
/// page number: the client only ever asks for "what comes after this", which costs one index seek
/// instead of a COUNT plus a growing OFFSET scan.
/// </summary>
/// <param name="Items">Rows in this page.</param>
/// <param name="NextCursor">Opaque cursor for the following page, or null when this is the last one.</param>
public sealed record CursorPage<T>(List<T> Items, string? NextCursor);

/// <summary>Entities that can be paged by cursor. Ordering is newest-first on <see cref="CreatedAt"/>.</summary>
public interface ICursorKeyed
{
    DateTime CreatedAt { get; }
}
