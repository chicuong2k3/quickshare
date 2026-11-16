namespace QuickShare.Features.Search;

public sealed class GlobalSearchRequest
{
    [QueryParam]
    public string? Q { get; set; }
}

public sealed record SearchFileHit(
    Guid Id,
    string FileName,
    string ShareCode,
    string ContentType,
    long FileSize,
    string? FolderName,
    string? Tag,
    string? Snippet,
    string MatchSource,
    DateTime CreatedAt);

public sealed record SearchFolderHit(
    Guid Id,
    string Name,
    Guid? ParentId,
    DateTime CreatedAt);

public sealed record GlobalSearchResponse(
    string Query,
    IReadOnlyList<SearchFileHit> Files,
    IReadOnlyList<SearchFolderHit> Folders);

/// <summary>
/// GET /api/search?q=... — full-text search across the user's own and workspace deliveries
/// (filename, tag, message, folder name, and indexed text/OCR content) plus workspace folders (issue #307).
///
/// Privacy mode (#187) deliveries are never content-indexed, and tenant isolation (#190) scopes
/// every query to the authenticated user's own shares and active workspace seat.
/// </summary>
public sealed class GlobalSearchEndpoint(AppDbContext db)
    : Endpoint<GlobalSearchRequest, GlobalSearchResponse>
{
    public const int MaxResultsPerGroup = 25;
    public const int SnippetLength = 120;

    public override void Configure()
    {
        Get(RoutePaths.SearchApi);
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Search files and folders by name, tags, messages, and indexed content";
            s.Description = "Issue #307. Returns grouped files and folders with highlighted snippets.";
        });
    }

    public override async Task HandleAsync(GlobalSearchRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var rawQuery = (req.Q ?? string.Empty).Trim();
        if (rawQuery.Length == 0)
        {
            await Send.OkAsync(new GlobalSearchResponse(string.Empty, [], []), ct);
            return;
        }

        var workspaceId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(ct);

#pragma warning disable CA1304, CA1311, CA1862
        var term = rawQuery.ToLower();

        var folders = workspaceId is null
            ? new List<SearchFolderHit>()
            : await db.WorkspaceFolders
                .AsNoTracking()
                .Where(f => f.WorkspaceId == workspaceId && f.Name.ToLower().Contains(term))
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Name)
                .Take(MaxResultsPerGroup)
                .Select(f => new SearchFolderHit(f.Id, f.Name, f.ParentId, f.CreatedAt))
                .ToListAsync(ct);

        var matchedDeliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.DeletedAt == null && d.Status != DeliveryStatus.Suspended)
            .Where(d => d.SenderId == user.Id || (workspaceId != null && d.WorkspaceId == workspaceId))
            .Where(d =>
                d.FileName.ToLower().Contains(term)
                || (d.Tag != null && d.Tag.ToLower().Contains(term))
                || (!d.PrivacyMode && d.Message != null && d.Message.ToLower().Contains(term))
                || (!d.PrivacyMode && db.DeliveryOcrTexts.Any(o => o.DeliveryId == d.Id && o.Text != null && o.Text.ToLower().Contains(term)))
                || (d.FolderId != null && db.WorkspaceFolders.Any(f => f.Id == d.FolderId && f.Name.ToLower().Contains(term))))
            .OrderByDescending(d => d.CreatedAt)
            .Take(MaxResultsPerGroup)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.ShareCode,
                d.ContentType,
                d.FileSize,
                d.FolderId,
                d.Tag,
                d.Message,
                d.PrivacyMode,
                d.CreatedAt,
            })
            .ToListAsync(ct);
#pragma warning restore CA1304, CA1311, CA1862

        var deliveryIds = matchedDeliveries.Select(d => d.Id).ToArray();
        var folderIds = matchedDeliveries.Where(d => d.FolderId.HasValue).Select(d => d.FolderId!.Value).Distinct().ToArray();

        var ocrMap = deliveryIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.DeliveryOcrTexts
                .AsNoTracking()
                .Where(o => deliveryIds.Contains(o.DeliveryId) && o.Text != null)
                .ToDictionaryAsync(o => o.DeliveryId, o => o.Text!, ct);

        var folderNameMap = folderIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.WorkspaceFolders
                .AsNoTracking()
                .Where(f => folderIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        var fileHits = new List<SearchFileHit>(matchedDeliveries.Count);
        foreach (var d in matchedDeliveries)
        {
            var folderName = d.FolderId.HasValue && folderNameMap.TryGetValue(d.FolderId.Value, out fn) ? fn : null;
            var source = "name";
            string? snippet = null;

            if (!d.PrivacyMode && ocrMap.TryGetValue(d.Id, out var contentText)
                && contentText.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            {
                source = "content";
                snippet = BuildSnippet(contentText, rawQuery);
            }
            else if (!d.PrivacyMode && !string.IsNullOrEmpty(d.Message)
                && d.Message.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            {
                source = "message";
                snippet = BuildSnippet(d.Message, rawQuery);
            }
            else if (!string.IsNullOrEmpty(d.Tag) && d.Tag.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            {
                source = "tag";
                snippet = d.Tag;
            }
            else if (!string.IsNullOrEmpty(folderName) && folderName.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            {
                source = "folder";
                snippet = folderName;
            }

            fileHits.Add(new SearchFileHit(
                d.Id,
                d.FileName,
                d.ShareCode,
                d.ContentType,
                d.FileSize,
                folderName,
                d.Tag,
                snippet,
                source,
                d.CreatedAt));
        }

        await Send.OkAsync(new GlobalSearchResponse(rawQuery, fileHits, folders), ct);
    }

    private string? fn;

    internal static string BuildSnippet(string fullText, string query)
    {
        var idx = fullText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return fullText.Length <= SnippetLength ? fullText : fullText[..SnippetLength] + "…";
        }

        var start = Math.Max(0, idx - 30);
        var len = Math.Min(SnippetLength, fullText.Length - start);
        var slice = fullText.Substring(start, len).Replace('\r', ' ').Replace('\n', ' ');
        return (start > 0 ? "…" : string.Empty) + slice + (start + len < fullText.Length ? "…" : string.Empty);
    }
}
