namespace QuickShare.Querying;

public sealed record SortDescriptor(string PropertyName, bool Descending = false)
{
    public static IEnumerable<SortDescriptor> Parse(string[]? sortParams, string defaultProperty = "CreatedAt")
    {
        if (sortParams is null || sortParams.Length == 0)
        {
            yield return new SortDescriptor(defaultProperty);
            yield break;
        }

        foreach (var sort in sortParams)
        {
            var parts = sort.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var property = parts[0];
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            yield return new SortDescriptor(property, descending);
        }
    }
}
