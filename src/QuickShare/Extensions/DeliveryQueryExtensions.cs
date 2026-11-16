namespace QuickShare.Extensions;

public static class DeliveryQueryExtensions
{
    /// <summary>
    /// Recipient-facing lookup: a /d/{code} segment may be the generated share code or
    /// a sender-chosen alias (issue #311). Aliases are stored lowercase.
    /// </summary>
    public static IQueryable<Delivery> ByCode(this IQueryable<Delivery> source, string code)
    {
        var alias = code.ToLowerInvariant();
        return source.Where(d => d.ShareCode == code || d.Alias == alias);
    }
}
