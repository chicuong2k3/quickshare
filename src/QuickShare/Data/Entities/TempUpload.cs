namespace QuickShare.Data.Entities;

/// <summary>
/// A file dropped into the app via the Android/Chrome Share Target (issue #14).
/// Saved server-side with a short TTL so /send can pick it up and run the
/// normal upload flow (progress + resume intact).
/// </summary>
public record TempUpload : BaseAuditEntity
{
    public const int TtlMinutes = 15;

    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
    public string TempPath { get; init; } = string.Empty;
}
