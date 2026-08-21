namespace QuickShare.Data.Entities;

public static class BackupFrequency
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static readonly string[] All = [Daily, Weekly];

    public static TimeSpan Interval(string frequency) =>
        frequency == Weekly ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
}

public static class BackupRunStatus
{
    public const string Never = "never";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

/// <summary>
/// A workspace's scheduled export to storage the customer controls (issue #271).
///
/// The question procurement asks is "what happens if your region burns down?", and the only
/// answer that survives follow-up is a copy the customer holds themselves. So the destination
/// is *their* S3-compatible bucket, with their credentials — a backup that lives in the same
/// blast radius as the thing it is backing up is not a backup.
///
/// What ships is a metadata manifest: workspace, seats, folders, delivery records. Deliberately
/// not the file bytes — copying terabytes into a bucket on a schedule is a bandwidth bill the
/// customer has not agreed to, and the manifest carries the storage keys they need to pull the
/// objects themselves. That limit is stated on the settings page rather than left to be
/// discovered during an actual disaster.
/// </summary>
public record WorkspaceBackupPolicy : BaseAuditEntity
{
    public const int EndpointMaxLength = 500;
    public const int BucketMaxLength = 200;
    public const int PrefixMaxLength = 200;
    public const int SecretMaxLength = 200;
    public const int ErrorMaxLength = 400;
    public const int KeyMaxLength = 400;
    public const int MinRetainedCopies = 1;
    public const int MaxRetainedCopies = 50;

    public Guid WorkspaceId { get; set; }
    public bool Enabled { get; set; } = true;
    public string Frequency { get; set; } = BackupFrequency.Weekly;
    /// <summary>Copies kept at the destination; older manifests are pruned after a successful run.</summary>
    public int RetainedCopies { get; set; } = 4;

    // ── Destination: the customer's own S3-compatible bucket ──
    public string Endpoint { get; set; } = string.Empty;
    public string Region { get; set; } = ObjectStoreDefaults.Region;
    public string BucketName { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string AccessKeySecret { get; set; } = string.Empty;

    // ── Last run ──
    public DateTime? LastRunAt { get; set; }
    public string LastStatus { get; set; } = BackupRunStatus.Never;
    public string? LastError { get; set; }
    public string? LastManifestKey { get; set; }
    public long LastManifestBytes { get; set; }
    public int LastDeliveryCount { get; set; }

    public Workspace Workspace { get; init; } = null!;

    /// <summary>Due when enabled, configured, and never run or run longer ago than the interval.</summary>
    public bool IsDue(DateTime now) =>
        Enabled
        && !string.IsNullOrEmpty(Endpoint)
        && !string.IsNullOrEmpty(BucketName)
        && (LastRunAt is null || now - LastRunAt >= BackupFrequency.Interval(Frequency));
}

/// <summary>Shared defaults for S3-compatible connection settings (issue #171).</summary>
public static class ObjectStoreDefaults
{
    public const string Region = "auto";
}
