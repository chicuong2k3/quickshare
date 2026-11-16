namespace QuickShare.Features.Uploads.ShareTarget;

/// <summary>
/// Temp-file storage for the Android/Chrome Share Target (issue #14). Files
/// land here with a short TTL and are swept by TempUploadCleanupJob; /send
/// picks them up and runs the normal upload flow.
/// </summary>
public static class ShareTargetService
{
    public const string TempDirName = "quickshare-share-target";

    public static string EnsureTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), TempDirName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string TempPathFor(Guid id) => Path.Combine(EnsureTempDir(), $"{id:N}.bin");
}
