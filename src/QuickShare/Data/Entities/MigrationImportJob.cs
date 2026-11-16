namespace QuickShare.Data.Entities;

/// <summary>
/// Guided migration job from external transfer/storage tools (WeTransfer, Google Drive, S3, or URL list)
/// into QuickShareCloud with folder mapping and conflict resolution (issue #272).
/// </summary>
public record MigrationImportJob : BaseAuditEntity
{
    public const int SourceMaxLength = 32;
    public const int ConflictPolicyMaxLength = 24;
    public const int ItemsJsonMaxLength = 16000;
    public const int MaxItemsPerJob = 50;

    public const string SourceWeTransfer = "wetransfer";
    public const string SourceGoogleDrive = "gdrive";
    public const string SourceS3 = "s3";
    public const string SourceUrls = "urls";

    public const string ConflictSkip = "skip";
    public const string ConflictRename = "rename";
    public const string ConflictReplace = "replace";

    public const string StatusDryRun = "dry_run";
    public const string StatusCompleted = "completed";
    public const string StatusCompletedWithErrors = "completed_with_errors";

    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? TargetFolderId { get; set; }
    public string SourceProvider { get; set; } = SourceWeTransfer;
    public string ConflictPolicy { get; set; } = ConflictRename;
    public int TotalItems { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string Status { get; set; } = StatusCompleted;
    public string SummaryJson { get; set; } = "[]";
}
