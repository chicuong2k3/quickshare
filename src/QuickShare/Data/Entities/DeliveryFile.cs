namespace QuickShare.Data.Entities;

/// <summary>
/// One file inside a delivery (issues #79, #43).
///
/// **Every delivery has at least one of these, including the single-file ones that predate the
/// feature** — the migration backfills them. That invariant is the whole design: without it,
/// every consumer would need to ask "is this the old shape or the new one?" and half of them
/// would get it wrong.
///
/// <see cref="Delivery"/> keeps describing file #1 exactly as before — same
/// <c>FileName</c>, <c>ContentType</c>, <c>FileSize</c>, <c>StoragePath</c>. That is not
/// duplication for its own sake: it means every path written before multi-file existed —
/// previews, virus scan, expiry, backup, SIEM, the download page's single-file phase — keeps
/// working untouched and correctly for the single-file case, which is still almost every
/// delivery. New code reads these rows; old code never had to be found and changed.
/// </summary>
public record DeliveryFile
{
    public const int FileNameMaxLength = 512;
    public const int ContentTypeMaxLength = 128;
    public const int Sha256Length = 64;

    /// <summary>Ceiling per delivery. Generous, but a bundle is not a filesystem.</summary>
    public const int MaxFilesPerDelivery = 100;

    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }

    /// <summary>
    /// Display name. May carry a forward-slash relative path for a folder upload (#43) —
    /// <c>photos/2026/beach.jpg</c> — so the zip rebuilds the folder the sender dragged in.
    /// Sanitised on the way in: no absolute paths, no <c>..</c>, no backslashes.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }

    /// <summary>Object key in the bucket. Always under this delivery's own prefix — checked at commit.</summary>
    public string ObjectKey { get; init; } = string.Empty;

    public string? Sha256 { get; init; }

    /// <summary>Sender's order. File 0 is the one <see cref="Delivery"/> mirrors.</summary>
    public int SortOrder { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Delivery Delivery { get; init; } = null!;

    /// <summary>
    /// Cleans a client-supplied name into something safe to store and to write into a zip.
    ///
    /// Zip path traversal ("zip slip") is a real and repeatedly-exploited class of bug: an entry
    /// named <c>../../etc/passwd</c> escapes the extraction directory in a careless client. We
    /// cannot fix every client, but we can refuse to be the one that ships the payload.
    /// </summary>
    public static string SanitizeName(string? raw, int fallbackIndex)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return $"file-{fallbackIndex + 1}";

        var normalized = raw.Replace('\\', '/').Trim();

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && s != "." && s != "..")
            // A drive letter or a leading root would make the entry absolute.
            .Where(s => !s.Contains(':', StringComparison.Ordinal))
            .Select(s => new string(s.Where(c => !char.IsControl(c)).ToArray()))
            .Where(s => s.Length > 0)
            .ToList();

        if (segments.Count == 0)
            return $"file-{fallbackIndex + 1}";

        var cleaned = string.Join('/', segments);
        return cleaned.Length > FileNameMaxLength ? cleaned[^FileNameMaxLength..] : cleaned;
    }
}
