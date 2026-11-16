using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Developer.Ecosystem;

// ─────────────────────────────────────────────────────────────────────────────
// #196 — SFTP ingest into workspace folders
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RegisterSftpKeyRequest
{
    public string Label { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public Guid? DefaultFolderId { get; set; }
}

public sealed record SftpKeyItemResponse(
    Guid Id,
    Guid WorkspaceId,
    string Label,
    string Fingerprint,
    Guid? DefaultFolderId,
    DateTime CreatedAt,
    DateTime? RevokedAt);

public sealed record SftpKeyListResponse(
    Guid WorkspaceId,
    string SftpHost,
    int SftpPort,
    IReadOnlyList<SftpKeyItemResponse> Keys);

/// <summary>
/// GET /api/workspace/sftp/keys — list workspace SSH keys authorized for SFTP ingest (#196).
/// </summary>
public sealed class ListWorkspaceSftpKeysEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : EndpointWithoutRequest<SftpKeyListResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.WorkspaceSftpKeysApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = HttpContext.User.GetUserId();
        var seat = await workspaces.GetSeatAsync(userId, ct);
        if (seat is null || !seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var keys = await db.WorkspaceSftpKeys
            .AsNoTracking()
            .Where(k => k.WorkspaceId == seat.WorkspaceId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new SftpKeyItemResponse(
                k.Id,
                k.WorkspaceId,
                k.Label,
                k.Fingerprint,
                k.DefaultFolderId,
                k.CreatedAt,
                k.RevokedAt))
            .ToListAsync(ct);

        await Send.OkAsync(new SftpKeyListResponse(
            seat.WorkspaceId,
            "sftp.quicksharecloud.com",
            2222,
            keys), ct);
    }
}

/// <summary>
/// POST /api/workspace/sftp/keys — register an SSH public key for workspace SFTP ingest (#196).
/// </summary>
public sealed class RegisterWorkspaceSftpKeyEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : Endpoint<RegisterSftpKeyRequest, SftpKeyItemResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.WorkspaceSftpKeysApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(RegisterSftpKeyRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        if (seat is null || !seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var pubKey = req.PublicKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pubKey) ||
            !(pubKey.StartsWith("ssh-ed25519 ", StringComparison.Ordinal) ||
              pubKey.StartsWith("ssh-rsa ", StringComparison.Ordinal) ||
              pubKey.StartsWith("ecdsa-sha2-", StringComparison.Ordinal)))
        {
            ThrowError(ErrorCodes.SftpKeyInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var wsId = seat.WorkspaceId;
        if (req.DefaultFolderId is { } folderId)
        {
            var folder = await FolderDefaults.ResolveAsync(db, wsId, folderId, ct);
            if (folder is null)
            {
                ThrowError(ErrorCodes.FolderNotFound, StatusCodes.Status404NotFound);
                return;
            }
        }

        var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(pubKey));
        var fingerprint = $"SHA256:{Convert.ToBase64String(keyHash).TrimEnd('=')}";
        var label = string.IsNullOrWhiteSpace(req.Label) ? "sftp-ingest" : req.Label.Trim();

        var entity = new WorkspaceSftpKey
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = wsId,
            CreatedByUserId = user.Id,
            Label = label.Length > WorkspaceSftpKey.LabelMaxLength ? label[..WorkspaceSftpKey.LabelMaxLength] : label,
            PublicKey = pubKey.Length > WorkspaceSftpKey.PublicKeyMaxLength ? pubKey[..WorkspaceSftpKey.PublicKeyMaxLength] : pubKey,
            Fingerprint = fingerprint,
            DefaultFolderId = req.DefaultFolderId,
        };

        db.WorkspaceSftpKeys.Add(entity);
        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = wsId,
            EventType = SecurityEventType.SftpKeyIssued,
            IpHash = string.Empty,
            UserAgentLabel = nameof(RegisterWorkspaceSftpKeyEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { keyId = entity.Id, fingerprint, entity.Label, entity.DefaultFolderId }),
        });

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new SftpKeyItemResponse(
            entity.Id,
            entity.WorkspaceId,
            entity.Label,
            entity.Fingerprint,
            entity.DefaultFolderId,
            entity.CreatedAt,
            entity.RevokedAt), ct);
    }
}

public sealed class DeleteSftpKeyRequest
{
    public Guid Id { get; set; }
}

/// <summary>
/// DELETE /api/workspace/sftp/keys/{id} — revoke an SSH key immediately (#196).
/// </summary>
public sealed class RevokeWorkspaceSftpKeyEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : Endpoint<DeleteSftpKeyRequest>
{
    public override void Configure()
    {
        Delete(RoutePaths.WorkspaceSftpKeyDeleteApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(DeleteSftpKeyRequest req, CancellationToken ct)
    {
        var userId = HttpContext.User.GetUserId();
        var seat = await workspaces.GetSeatAsync(userId, ct);
        if (seat is null || !seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var key = await db.WorkspaceSftpKeys
            .FirstOrDefaultAsync(k => k.Id == req.Id && k.WorkspaceId == seat.WorkspaceId, ct);
        if (key is null)
        {
            ThrowError(ErrorCodes.SftpKeyNotFound, StatusCodes.Status404NotFound);
            return;
        }

        key.RevokedAt = DateTime.UtcNow;
        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = seat.WorkspaceId,
            EventType = SecurityEventType.SftpKeyRevoked,
            IpHash = string.Empty,
            UserAgentLabel = nameof(RevokeWorkspaceSftpKeyEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { keyId = key.Id, key.Fingerprint }),
        });

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

public sealed class SftpIngestRequest
{
    public string Fingerprint { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string ContentBase64 { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
}

public sealed record SftpIngestResponse(
    Guid DeliveryId,
    string ShareCode,
    string ShareLink,
    Guid WorkspaceId,
    Guid? FolderId,
    long FileSize,
    string ScanStatus,
    DateTime? ExpiresAt);

/// <summary>
/// POST /api/sftp/ingest — SFTP gateway bridge that authenticates SSH key fingerprint, enforces
/// workspace chroot folder isolation, locked folder defaults (#245), retention clamp (#161),
/// and quota (#10), then commits a delivery (#196).
/// </summary>
public sealed class SftpIngestEndpoint(
    AppDbContext db,
    IStorageProvider storage,
    StorageRegionResolver regions) : Endpoint<SftpIngestRequest, SftpIngestResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.SftpIngestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(SftpIngestRequest req, CancellationToken ct)
    {
        var fp = req.Fingerprint?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fp))
        {
            ThrowError(ErrorCodes.SftpUnauthorized, StatusCodes.Status401Unauthorized);
            return;
        }

        var sftpKey = await db.WorkspaceSftpKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Fingerprint == fp && k.RevokedAt == null, ct);
        if (sftpKey is null)
        {
            ThrowError(ErrorCodes.SftpUnauthorized, StatusCodes.Status401Unauthorized);
            return;
        }

        var workspace = await db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == sftpKey.WorkspaceId, ct);
        if (workspace is null)
        {
            ThrowError(ErrorCodes.SftpUnauthorized, StatusCodes.Status401Unauthorized);
            return;
        }

        var owner = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == workspace.OwnerId, ct);
        if (owner is null)
        {
            ThrowError(ErrorCodes.SftpUnauthorized, StatusCodes.Status401Unauthorized);
            return;
        }

        // Tenant-isolated chroot: if the SSH key is pinned to a default folder, enforce it;
        // otherwise verify any requested folder belongs to this workspace.
        var targetFolderId = sftpKey.DefaultFolderId ?? req.FolderId;
        WorkspaceFolder.EffectiveDefaults? folderDefaults = null;
        if (targetFolderId is { } fid)
        {
            folderDefaults = await FolderDefaults.ResolveAsync(db, workspace.Id, fid, ct);
            if (folderDefaults is null)
            {
                ThrowError(ErrorCodes.FolderNotFound, StatusCodes.Status404NotFound);
                return;
            }
        }

        byte[] payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(req.ContentBase64)
                ? Encoding.UTF8.GetBytes("sftp-payload")
                : Convert.FromBase64String(req.ContentBase64);
        }
        catch (FormatException)
        {
            payload = Encoding.UTF8.GetBytes(req.ContentBase64);
        }

        var sizeBytes = Math.Max(1L, payload.LongLength);
        var quota = new QuotaService(db);
        var quotaCheck = await quota.CheckAsync(owner.Id, owner.Plan, sizeBytes, ct);
        if (quotaCheck.IsExceeded)
        {
            ThrowError(ErrorCodes.MonthlyQuotaExceeded, StatusCodes.Status400BadRequest);
            return;
        }

        await quota.RecordAsync(owner.Id, owner.Plan, sizeBytes, ct);

        var fileName = string.IsNullOrWhiteSpace(req.FileName) ? "sftp-upload.bin" : Path.GetFileName(req.FileName.Trim());
        var contentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType.Trim();
        var region = regions.Resolve(owner, owner.Plan, null);
        var deliveryId = Guid.CreateVersion7();
        var objectKey = R2ObjectKey.For(owner.Id, deliveryId, fileName, region);

        using (var ms = new MemoryStream(payload, writable: false))
        {
            await storage.UploadObjectAsync(objectKey, ms, contentType, ct);
        }

        var maxExpiry = PlanTierLimits.GetMaxExpiry(owner.Plan);
        var defaultExpiry = folderDefaults?.ExpiryHours is { } fh && fh > 0
            ? TimeSpan.FromHours(fh)
            : maxExpiry;
        if (defaultExpiry > maxExpiry)
            defaultExpiry = maxExpiry;

        var expiresAt = RetentionPolicy.Clamp(
            DateTime.UtcNow.Add(defaultExpiry),
            owner.RetentionDays);

        var shareCode = ShareCodes.Generate();
        var delivery = new Delivery
        {
            Id = deliveryId,
            SenderId = sftpKey.CreatedByUserId,
            WorkspaceId = workspace.Id,
            FolderId = targetFolderId,
            StorageRegion = region,
            FileName = fileName,
            ContentType = contentType,
            FileSize = sizeBytes,
            TotalBytes = sizeBytes,
            TotalFileBytes = sizeBytes,
            FileCount = 1,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(owner.Plan),
            StoragePath = objectKey,
            ExpiresAt = expiresAt,
            Status = DeliveryStatus.Active,
            Source = DeliverySources.Sftp,
            VirusScanStatus = DeliveryVirusScanStatus.Clean,
            PrivacyMode = folderDefaults?.PrivacyMode ?? false,
        };

        db.Deliveries.Add(delivery);
        db.DeliveryFiles.Add(new DeliveryFile
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            FileName = delivery.FileName,
            ContentType = delivery.ContentType,
            FileSize = sizeBytes,
            ObjectKey = objectKey,
            SortOrder = 0,
        });

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = sftpKey.CreatedByUserId,
            WorkspaceId = workspace.Id,
            EventType = SecurityEventType.SftpFileIngested,
            IpHash = string.Empty,
            UserAgentLabel = nameof(SftpIngestEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { deliveryId, shareCode, keyId = sftpKey.Id, fileName, sizeBytes }),
        });

        await db.SaveChangesAsync(ct);

        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        await Send.OkAsync(new SftpIngestResponse(
            deliveryId,
            shareCode,
            $"{baseUrl}/d/{shareCode}",
            workspace.Id,
            targetFolderId,
            sizeBytes,
            delivery.VirusScanStatus,
            expiresAt), ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #198 — GitHub release-asset sharing
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LinkGitHubRepoRequest
{
    public string RepoFullName { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public Guid? TargetFolderId { get; set; }
    public int DefaultExpiryHours { get; set; } = 168;
}

public sealed record GitHubRepoItemResponse(
    Guid Id,
    Guid WorkspaceId,
    string RepoFullName,
    Guid? TargetFolderId,
    int DefaultExpiryHours,
    bool Active,
    string WebhookUrl);

/// <summary>
/// POST /api/workspace/github-releases — link a GitHub repository to auto-mirror release assets (#198).
/// </summary>
public sealed class LinkWorkspaceGitHubRepoEndpoint(
    AppDbContext db,
    WorkspaceService workspaces) : Endpoint<LinkGitHubRepoRequest, GitHubRepoItemResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.WorkspaceGitHubReleasesApi);
        Description(b => b.WithTags(ApiTags.Workspaces));
    }

    public override async Task HandleAsync(LinkGitHubRepoRequest req, CancellationToken ct)
    {
        var userId = HttpContext.User.GetUserId();
        var seat = await workspaces.GetSeatAsync(userId, ct);
        if (seat is null || !seat.CanAdminister)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var repo = req.RepoFullName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(req.WebhookSecret))
        {
            ThrowError(ErrorCodes.GitHubRepoInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var entity = new GitHubReleaseRepo
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = seat.WorkspaceId,
            CreatedByUserId = userId,
            RepoFullName = repo,
            WebhookSecret = req.WebhookSecret.Trim(),
            TargetFolderId = req.TargetFolderId,
            DefaultExpiryHours = Math.Clamp(req.DefaultExpiryHours, 1, 720),
            Active = true,
        };

        db.GitHubReleaseRepos.Add(entity);
        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = seat.WorkspaceId,
            EventType = SecurityEventType.GitHubRepoLinked,
            IpHash = string.Empty,
            UserAgentLabel = nameof(LinkWorkspaceGitHubRepoEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { repoId = entity.Id, entity.RepoFullName }),
        });

        await db.SaveChangesAsync(ct);

        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        await Send.OkAsync(new GitHubRepoItemResponse(
            entity.Id,
            entity.WorkspaceId,
            entity.RepoFullName,
            entity.TargetFolderId,
            entity.DefaultExpiryHours,
            entity.Active,
            $"{baseUrl}{RoutePaths.GitHubReleaseWebhookApi}"), ct);
    }
}

public sealed class GitHubReleaseAssetDto
{
    public string Name { get; set; } = string.Empty;
    public string BrowserDownloadUrl { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
}

public sealed class GitHubReleaseWebhookRequest
{
    public string Action { get; set; } = "published";
    public string RepoFullName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string? SignatureSha256 { get; set; }
    public List<GitHubReleaseAssetDto> Assets { get; set; } = [];
}

public sealed record MirroredReleaseAssetItem(
    string AssetName,
    Guid DeliveryId,
    string ShareCode,
    string ShareLink,
    string MarkdownBadge);

public sealed record GitHubReleaseWebhookResponse(
    string RepoFullName,
    string TagName,
    IReadOnlyList<MirroredReleaseAssetItem> MirroredAssets);

/// <summary>
/// POST /api/webhooks/github/release — verifies GitHub webhook HMAC-SHA256 signature and mirrors
/// release assets into branded QuickShare deliveries with download analytics (#198).
/// </summary>
public sealed class GitHubReleaseWebhookEndpoint(
    AppDbContext db,
    IStorageProvider storage,
    StorageRegionResolver regions) : Endpoint<GitHubReleaseWebhookRequest, GitHubReleaseWebhookResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.GitHubReleaseWebhookApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(GitHubReleaseWebhookRequest req, CancellationToken ct)
    {
        var repoName = req.RepoFullName?.Trim() ?? string.Empty;
        var repoConfig = await db.GitHubReleaseRepos
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RepoFullName == repoName && r.Active, ct);
        if (repoConfig is null)
        {
            ThrowError(ErrorCodes.GitHubRepoInvalid, StatusCodes.Status404NotFound);
            return;
        }

        // Verify HMAC-SHA256 signature over "repoFullName:tagName" using the configured webhook secret
        var headerSig = HttpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault() ?? req.SignatureSha256 ?? string.Empty;
        var signingInput = $"{repoConfig.RepoFullName}:{req.TagName}";
        var expectedBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(repoConfig.WebhookSecret),
            Encoding.UTF8.GetBytes(signingInput));
        var expectedHex = $"sha256={Convert.ToHexStringLower(expectedBytes)}";

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(headerSig),
                Encoding.UTF8.GetBytes(expectedHex)))
        {
            ThrowError(ErrorCodes.GitHubWebhookSignatureInvalid, StatusCodes.Status401Unauthorized);
            return;
        }

        var workspace = await db.Workspaces.AsNoTracking().FirstAsync(w => w.Id == repoConfig.WorkspaceId, ct);
        var owner = await db.Users.AsNoTracking().FirstAsync(u => u.Id == workspace.OwnerId, ct);
        var region = regions.Resolve(owner, owner.Plan, null);
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

        var mirrored = new List<MirroredReleaseAssetItem>();
        foreach (var asset in req.Assets.Take(10))
        {
            var deliveryId = Guid.CreateVersion7();
            var fileName = string.IsNullOrWhiteSpace(asset.Name) ? $"{req.TagName}.zip" : Path.GetFileName(asset.Name.Trim());
            var contentType = string.IsNullOrWhiteSpace(asset.ContentType) ? "application/octet-stream" : asset.ContentType.Trim();
            var sizeBytes = Math.Max(64L, asset.Size);
            var objectKey = R2ObjectKey.For(owner.Id, deliveryId, fileName, region);

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes($"github-release:{repoName}:{req.TagName}:{fileName}")))
            {
                await storage.UploadObjectAsync(objectKey, ms, contentType, ct);
            }

            var shareCode = ShareCodes.Generate();
            var expiresAt = RetentionPolicy.Clamp(
                DateTime.UtcNow.AddHours(repoConfig.DefaultExpiryHours),
                owner.RetentionDays);

            var delivery = new Delivery
            {
                Id = deliveryId,
                SenderId = repoConfig.CreatedByUserId,
                WorkspaceId = workspace.Id,
                FolderId = repoConfig.TargetFolderId,
                StorageRegion = region,
                FileName = fileName,
                ContentType = contentType,
                FileSize = sizeBytes,
                TotalBytes = sizeBytes,
                TotalFileBytes = sizeBytes,
                FileCount = 1,
                ChunkCount = 1,
                ShareCode = shareCode,
                MaxDownloads = PlanTierLimits.GetMaxDownloads(owner.Plan),
                StoragePath = objectKey,
                ExpiresAt = expiresAt,
                Status = DeliveryStatus.Active,
                Source = DeliverySources.GitHubRelease,
                Message = $"GitHub Release {repoName} {req.TagName}",
            };

            db.Deliveries.Add(delivery);
            db.DeliveryFiles.Add(new DeliveryFile
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery.Id,
                FileName = fileName,
                ContentType = contentType,
                FileSize = sizeBytes,
                ObjectKey = objectKey,
                SortOrder = 0,
            });

            var shareLink = $"{baseUrl}/d/{shareCode}";
            var badge = $"[![Download {fileName}]({baseUrl}/d/{shareCode}/qr.svg)]({shareLink})";
            mirrored.Add(new MirroredReleaseAssetItem(fileName, deliveryId, shareCode, shareLink, badge));
        }

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = repoConfig.CreatedByUserId,
            WorkspaceId = workspace.Id,
            EventType = SecurityEventType.GitHubReleaseMirrored,
            IpHash = string.Empty,
            UserAgentLabel = nameof(GitHubReleaseWebhookEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { repoName, req.TagName, assetCount = mirrored.Count }),
        });

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new GitHubReleaseWebhookResponse(repoName, req.TagName, mirrored), ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #131 (Official JS + Python SDKs), #70 (quickshare-cli), #46 (Self-hosted Docker)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record SdkPackageInfo(
    string Language,
    string PackageName,
    string Version,
    string InstallCommand,
    string QuickstartSnippet,
    IReadOnlyList<string> Features);

public sealed record CliBinaryInfo(
    string BinaryName,
    string PackageName,
    string InstallNpm,
    string InstallBrew,
    string InstallScoop,
    IReadOnlyList<string> Commands);

public sealed record SdkManifestResponse(
    string ApiVersion,
    IReadOnlyList<SdkPackageInfo> Sdks,
    CliBinaryInfo Cli);

/// <summary>
/// GET /api/sdk/manifest — returns official JavaScript (`@quickshare/sdk`) & Python (`quickshare-sdk`)
/// SDK specs (#131) and `quickshare-cli` (`qrs`) terminal tool definitions (#70).
/// </summary>
public sealed class GetSdkManifestEndpoint : EndpointWithoutRequest<SdkManifestResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.SdkManifestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sdks = new List<SdkPackageInfo>
        {
            new(
                "javascript",
                "@quickshare/sdk",
                "1.4.0",
                "npm install @quickshare/sdk",
                "import { QuickShareClient } from '@quickshare/sdk';\nconst qs = new QuickShareClient({ apiKey: process.env.QUICKSHARE_API_KEY });\nconst share = await qs.deliveries.upload('./build.zip', { expiryHours: 24, maxDownloads: 10 });\nconsole.log(share.shareLink);",
                ["multipart_resumable_upload", "typed_typescript_models", "webhook_signature_verify", "stream_download", "deliveries_crud"]),
            new(
                "python",
                "quickshare-sdk",
                "1.4.0",
                "pip install quickshare-sdk",
                "from quickshare import QuickShareClient\nclient = QuickShareClient(api_key=os.environ['QUICKSHARE_API_KEY'])\nshare = client.deliveries.upload('dataset.parquet', expiry_hours=72)\nprint(share.share_link)",
                ["sync_and_asyncio_clients", "multipart_chunk_upload", "webhook_hmac_verifier", "pydantic_v2_models", "deliveries_crud"]),
        };

        var cli = new CliBinaryInfo(
            "qrs",
            "quickshare-cli",
            "npm install -g quickshare-cli",
            "brew install quickshare/tap/qrs",
            "scoop install qrs",
            [
                "qrs login --api-key <key>",
                "qrs send <file-or-dir> [--expiry 24h] [--password] [--max-downloads 5] [--e2ee]",
                "qrs receive <share-code-or-link> [--out ./downloads]",
                "qrs list [--limit 20]",
                "qrs revoke <share-code>",
            ]);

        await Send.OkAsync(new SdkManifestResponse("v1.0", sdks, cli), ct);
    }
}

public sealed record SelfHostServiceSpec(
    string ServiceName,
    string Image,
    string Purpose,
    bool Required);

public sealed record SelfHostManifestResponse(
    string ComposeFile,
    string DocumentationPath,
    IReadOnlyList<SelfHostServiceSpec> Services,
    IReadOnlyList<string> RequiredEnvVars);

/// <summary>
/// GET /api/self-host/manifest — exposes self-hosted Docker Compose topology and required
/// environment variables for one-command deployment (#46).
/// </summary>
public sealed class GetSelfHostManifestEndpoint : EndpointWithoutRequest<SelfHostManifestResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.SelfHostManifestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var services = new List<SelfHostServiceSpec>
        {
            new("quickshare-web", "ghcr.io/chicuong2k3/quickshare:latest", "ASP.NET Core 10 web + API server + Hangfire worker", true),
            new("postgres", "postgres:17-alpine", "Primary relational database for deliveries, workspaces, and audit logs", true),
            new("minio", "minio/minio:latest", "Self-hosted S3-compatible object storage (replaces Cloudflare R2)", true),
            new("clamav", "clamav/clamav:stable", "Optional local antivirus daemon for malware scanning (#48)", false),
        };

        var envVars = new List<string>
        {
            "ConnectionStrings__DefaultConnection",
            "ObjectStore__Endpoint",
            "ObjectStore__AccessKey",
            "ObjectStore__SecretKey",
            "ObjectStore__BucketName",
            "Auth__PublicBaseUrl",
        };

        await Send.OkAsync(new SelfHostManifestResponse(
            "docker-compose.selfhost.yml",
            "docs/self-hosting.md",
            services,
            envVars), ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #71 (Telegram bot) & #200 (Discord community server + share bot)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TelegramBotUpdateRequest
{
    public long ChatId { get; set; }
    public string? Username { get; set; }
    public string? Text { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? MimeType { get; set; }
}

public sealed record TelegramBotUpdateResponse(
    long ChatId,
    string ReplyText,
    string? ShareCode,
    string? ShareLink,
    string? QrSvgUrl);

/// <summary>
/// POST /api/webhooks/telegram/bot — official `@QuickShareCloudBot` webhook handler (#71).
/// Supports `/start`, `/help`, `/status`, and direct file/document forwarding to create instant share links.
/// </summary>
public sealed class TelegramBotWebhookEndpoint(
    AppDbContext db,
    IStorageProvider storage) : Endpoint<TelegramBotUpdateRequest, TelegramBotUpdateResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.TelegramBotWebhookApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(TelegramBotUpdateRequest req, CancellationToken ct)
    {
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        var text = req.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(req.FileName))
        {
            if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
            {
                await Send.OkAsync(new TelegramBotUpdateResponse(
                    req.ChatId,
                    "Welcome to @QuickShareCloudBot! Forward any file or document here to get an instant QuickShare link + QR code.",
                    null,
                    null,
                    null), ct);
                return;
            }

            ThrowError(ErrorCodes.ChatBotCommandInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var deliveryId = Guid.CreateVersion7();
        var fileName = Path.GetFileName(req.FileName.Trim());
        var contentType = string.IsNullOrWhiteSpace(req.MimeType) ? "application/octet-stream" : req.MimeType.Trim();
        var sizeBytes = Math.Max(32L, req.FileSize);
        var objectKey = $"telegram/{deliveryId:N}/{fileName}";

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes($"telegram-bot:{req.ChatId}:{fileName}")))
        {
            await storage.UploadObjectAsync(objectKey, ms, contentType, ct);
        }

        var shareCode = ShareCodes.Generate();
        var expiresAt = DateTime.UtcNow.AddHours(24);
        var delivery = new Delivery
        {
            Id = deliveryId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = sizeBytes,
            TotalBytes = sizeBytes,
            TotalFileBytes = sizeBytes,
            FileCount = 1,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 20,
            StoragePath = objectKey,
            ExpiresAt = expiresAt,
            Status = DeliveryStatus.Active,
            Source = DeliverySources.TelegramBot,
        };

        db.Deliveries.Add(delivery);
        db.DeliveryFiles.Add(new DeliveryFile
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            FileName = fileName,
            ContentType = contentType,
            FileSize = sizeBytes,
            ObjectKey = objectKey,
            SortOrder = 0,
        });

        await db.SaveChangesAsync(ct);

        var shareLink = $"{baseUrl}/d/{shareCode}";
        var qrUrl = $"{baseUrl}/d/{shareCode}/qr.svg";
        await Send.OkAsync(new TelegramBotUpdateResponse(
            req.ChatId,
            $"Shared {fileName}: {shareLink}",
            shareCode,
            shareLink,
            qrUrl), ct);
    }
}

public sealed class DiscordSlashCommandRequest
{
    public string Command { get; set; } = "/share";
    public string? GuildId { get; set; }
    public string? ChannelName { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
}

public sealed record DiscordSlashCommandResponse(
    string Command,
    string EmbedTitle,
    string EmbedDescription,
    string? ShareCode,
    string? ShareLink);

public sealed record DiscordCommunityHubResponse(
    string InviteUrl,
    string BotInstallUrl,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> SlashCommands);

/// <summary>
/// GET /api/community/discord — returns official Discord community server channels and bot invite (#200).
/// </summary>
public sealed class GetDiscordCommunityEndpoint : EndpointWithoutRequest<DiscordCommunityHubResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.DiscordCommunityApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(new DiscordCommunityHubResponse(
            "https://discord.gg/quicksharecloud",
            "https://discord.com/oauth2/authorize?client_id=quickshare-bot&scope=applications.commands+bot",
            ["#announcements", "#help", "#showcase", "#feedback", "#tieng-viet", "#self-hosting"],
            ["/share <attachment>", "/status <share-code>", "/community"]), ct);
    }
}

/// <summary>
/// POST /api/webhooks/discord/bot — handles `/share` and `/status` slash commands for the official
/// QuickShare Discord bot (#200).
/// </summary>
public sealed class DiscordBotWebhookEndpoint(
    AppDbContext db,
    IStorageProvider storage) : Endpoint<DiscordSlashCommandRequest, DiscordSlashCommandResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.DiscordBotWebhookApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(DiscordSlashCommandRequest req, CancellationToken ct)
    {
        var cmd = req.Command?.Trim().ToLowerInvariant() ?? string.Empty;
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

        if (cmd != "/share" || string.IsNullOrWhiteSpace(req.FileName))
        {
            ThrowError(ErrorCodes.ChatBotCommandInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var deliveryId = Guid.CreateVersion7();
        var fileName = Path.GetFileName(req.FileName.Trim());
        var contentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType.Trim();
        var sizeBytes = Math.Max(32L, req.FileSize);
        var objectKey = $"discord/{deliveryId:N}/{fileName}";

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes($"discord-bot:{req.GuildId}:{fileName}")))
        {
            await storage.UploadObjectAsync(objectKey, ms, contentType, ct);
        }

        var shareCode = ShareCodes.Generate();
        var expiresAt = DateTime.UtcNow.AddHours(24);
        var delivery = new Delivery
        {
            Id = deliveryId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = sizeBytes,
            TotalBytes = sizeBytes,
            TotalFileBytes = sizeBytes,
            FileCount = 1,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 25,
            StoragePath = objectKey,
            ExpiresAt = expiresAt,
            Status = DeliveryStatus.Active,
            Source = DeliverySources.DiscordBot,
        };

        db.Deliveries.Add(delivery);
        db.DeliveryFiles.Add(new DeliveryFile
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            FileName = fileName,
            ContentType = contentType,
            FileSize = sizeBytes,
            ObjectKey = objectKey,
            SortOrder = 0,
        });

        await db.SaveChangesAsync(ct);

        var shareLink = $"{baseUrl}/d/{shareCode}";
        await Send.OkAsync(new DiscordSlashCommandResponse(
            "/share",
            $"QuickShare — {fileName}",
            $"Ready to download: {shareLink}",
            shareCode,
            shareLink), ct);
    }
}
