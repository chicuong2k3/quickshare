namespace QuickShare.Server.Infrastructure.Storage;

public sealed class R2Options
{
    public const string Section = "R2";

    public string AccountId { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string AccessKeySecret { get; init; } = string.Empty;
    public int MultipartThreshold { get; init; } = 10 * 1024 * 1024; // 10 MB

    public PresignedUrlExpirySettings PresignedUrlExpiry { get; init; } = new();
}

public sealed class PresignedUrlExpirySettings
{
    public int UploadPartMinutes { get; init; } = 15;
    public int DownloadSeconds { get; init; } = 60;
}
