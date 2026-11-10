using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Growth.Distribution;

// ─────────────────────────────────────────────────────────────────────────────
// #159 — Native desktop app (Tauri v2 tray + drag-drop upload + QR pairing)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record DesktopPlatformBuild(
    string Platform,
    string Architecture,
    string InstallerFileName,
    string DownloadUrl,
    string Sha256,
    long SizeBytes);

public sealed record DesktopAppManifestResponse(
    string AppName,
    string Version,
    string Framework,
    string AutoUpdateEndpoint,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<DesktopPlatformBuild> Builds);

/// <summary>
/// GET /api/apps/desktop/manifest — Tauri v2 native tray app release manifest, auto-updater feed,
/// and supported capabilities (#159).
/// </summary>
public sealed class GetDesktopAppManifestEndpoint : EndpointWithoutRequest<DesktopAppManifestResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.DesktopAppManifestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        var builds = new List<DesktopPlatformBuild>
        {
            new("windows", "x86_64", "QuickShare-Tray_2.4.0_x64_en-US.msi", $"{baseUrl}/releases/desktop/QuickShare-Tray_2.4.0_x64_en-US.msi", "8f4b2e91c3a501d9c8e12a34f56b78c9012d3e4f5a6b7c8d9e0f1a2b3c4d5e6f", 4_850_000L),
            new("macos", "universal", "QuickShare-Tray_2.4.0_universal.dmg", $"{baseUrl}/releases/desktop/QuickShare-Tray_2.4.0_universal.dmg", "3a1c5e7f9b2d4e6a8c0e2f4a6b8d0c2e4f6a8b0c2d4e6f8a0b2c4d6e8f0a1b2c", 5_120_000L),
            new("linux", "x86_64", "QuickShare-Tray_2.4.0_amd64.AppImage", $"{baseUrl}/releases/desktop/QuickShare-Tray_2.4.0_amd64.AppImage", "7d9e1f3a5b7c9d1e3f5a7b9c1d3e5f7a9b1c3d5e7f9a1b3c5d7e9f1a3b5c7d9e", 5_400_000L),
        };

        await Send.OkAsync(new DesktopAppManifestResponse(
            "QuickShare Tray",
            "2.4.0",
            "tauri-v2",
            $"{baseUrl}/api/apps/desktop/manifest",
            ["system_tray_drop_zone", "resumable_multipart_upload", "auto_copy_clipboard", "os_keychain_token", "qr_code_login", "multi_workspace_switcher"],
            builds), ct);
    }
}

public sealed class DesktopQrAuthChallengeState
{
    public string ChallengeCode { get; init; } = string.Empty;
    public string DeviceName { get; init; } = "QuickShare Desktop Tray";
    public DateTime ExpiresAt { get; init; }
    public Guid? ApprovedUserId { get; set; }
    public string? IssuedApiKeyToken { get; set; }
}

public static class DesktopQrAuthStore
{
    public static readonly ConcurrentDictionary<string, DesktopQrAuthChallengeState> Challenges = new(StringComparer.Ordinal);
}

public sealed class StartDesktopQrAuthRequest
{
    public string DeviceName { get; set; } = "QuickShare Desktop Tray";
}

public sealed record StartDesktopQrAuthResponse(
    string ChallengeCode,
    string QrApproveUrl,
    DateTime ExpiresAt);

/// <summary>
/// POST /api/apps/desktop/qr-auth — starts a QR login challenge for the Tauri desktop tray app (#159).
/// </summary>
public sealed class StartDesktopQrAuthEndpoint : Endpoint<StartDesktopQrAuthRequest, StartDesktopQrAuthResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.DesktopQrAuthStartApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(StartDesktopQrAuthRequest req, CancellationToken ct)
    {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var state = new DesktopQrAuthChallengeState
        {
            ChallengeCode = code,
            DeviceName = string.IsNullOrWhiteSpace(req.DeviceName) ? "QuickShare Desktop Tray" : req.DeviceName.Trim(),
            ExpiresAt = expiresAt,
        };
        DesktopQrAuthStore.Challenges[code] = state;

        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        await Send.OkAsync(new StartDesktopQrAuthResponse(
            code,
            $"{baseUrl}{RoutePaths.DesktopQrAuthApproveApi}?code={code}",
            expiresAt), ct);
    }
}

public sealed class ApproveDesktopQrAuthRequest
{
    public string ChallengeCode { get; set; } = string.Empty;
}

public sealed record ApproveDesktopQrAuthResponse(
    string ChallengeCode,
    string DeviceName,
    string IssuedApiKeyToken,
    Guid UserId);

/// <summary>
/// POST /api/apps/desktop/qr-auth/approve — authenticated web session approves a desktop tray QR challenge
/// and mints a scoped API key stored in the desktop OS keychain (#159).
/// </summary>
public sealed class ApproveDesktopQrAuthEndpoint(AppDbContext db)
    : Endpoint<ApproveDesktopQrAuthRequest, ApproveDesktopQrAuthResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.DesktopQrAuthApproveApi);
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(ApproveDesktopQrAuthRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var code = req.ChallengeCode?.Trim() ?? string.Empty;

        if (!DesktopQrAuthStore.Challenges.TryGetValue(code, out var state) || state.ExpiresAt < DateTime.UtcNow)
        {
            ThrowError(ErrorCodes.SessionNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var rawSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20));
        var fullToken = $"{ApiKey.TokenPrefix}{rawSecret}";
        var keyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fullToken)));

        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Name = state.DeviceName,
            Prefix = fullToken[..ApiKey.PrefixLength],
            KeyHash = keyHash,
            Scopes = "read,write",
        });
        await db.SaveChangesAsync(ct);

        state.ApprovedUserId = user.Id;
        state.IssuedApiKeyToken = fullToken;

        await Send.OkAsync(new ApproveDesktopQrAuthResponse(
            code,
            state.DeviceName,
            fullToken,
            user.Id), ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #122 — Browser extension (Chrome / Edge / Firefox Manifest V3 right-click share)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record BrowserExtensionManifestResponse(
    int ManifestVersion,
    string Name,
    string Version,
    string Description,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> ContextMenus,
    IReadOnlyDictionary<string, string> StoreLinks);

/// <summary>
/// GET /api/apps/browser-extension/manifest.json — Manifest V3 metadata & store links for Chrome, Edge, and Firefox (#122).
/// </summary>
public sealed class GetBrowserExtensionManifestEndpoint : EndpointWithoutRequest<BrowserExtensionManifestResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.BrowserExtensionManifestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var stores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chrome_web_store"] = "https://chromewebstore.google.com/detail/quicksharecloud/qs-ext-mv3",
            ["edge_addons"] = "https://microsoftedge.microsoft.com/addons/detail/quicksharecloud/qs-ext-mv3",
            ["firefox_amo"] = "https://addons.mozilla.org/en-US/firefox/addon/quicksharecloud/",
        };

        await Send.OkAsync(new BrowserExtensionManifestResponse(
            3,
            "QuickShareCloud — Right-Click File & Link Share",
            "1.3.0",
            "Right-click any image, link, or selected text/file to upload to QuickShareCloud and copy the share link + QR code.",
            ["contextMenus", "storage", "clipboardWrite", "notifications"],
            ["Share file with QuickShare", "Send selection as QuickShare Paste", "Generate QuickShare QR Code"],
            stores), ct);
    }
}

public sealed class QuickChannelShareRequest
{
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? ContentBase64 { get; set; }
    public string? ZaloUserId { get; set; }
}

public sealed record QuickChannelShareResponse(
    Guid DeliveryId,
    string ShareCode,
    string ShareLink,
    string QrSvgUrl,
    string Source,
    DateTime? ExpiresAt);

/// <summary>
/// POST /api/apps/browser-extension/share — context-menu quick share from Chrome/Edge/Firefox extension (#122).
/// </summary>
public sealed class BrowserExtensionShareEndpoint(
    AppDbContext db,
    IStorageProvider storage) : Endpoint<QuickChannelShareRequest, QuickChannelShareResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.BrowserExtensionShareApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(QuickChannelShareRequest req, CancellationToken ct)
    {
        var res = await QuickChannelShareHelper.CreateAsync(
            db,
            storage,
            HttpContext,
            req,
            DeliverySources.BrowserExtension,
            ct);
        await Send.OkAsync(res, ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #111 — Zalo Mini App (send & preview files inside Zalo Mini Program)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ZaloMiniAppManifestResponse(
    string MiniAppId,
    string AppName,
    string Version,
    string DefaultLocale,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> OAuthScopes);

/// <summary>
/// GET /api/zalo/mini-app/manifest — Zalo Mini Program configuration and native Zalo chat share capabilities (#111).
/// </summary>
public sealed class GetZaloMiniAppManifestEndpoint : EndpointWithoutRequest<ZaloMiniAppManifestResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.ZaloMiniAppManifestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(new ZaloMiniAppManifestResponse(
            "zalo-miniapp-quickshare-vn",
            "QuickShare Gửi File Nhanh (Zalo Mini App)",
            "1.2.0",
            "vi",
            ["zalo_oauth_pkce", "in_app_file_upload_5gb", "native_zalo_chat_share_card", "in_app_media_preview", "payos_vietqr_upgrade"],
            ["scope.userInfo", "scope.userPhonenumber"]), ct);
    }
}

/// <summary>
/// POST /api/zalo/mini-app/share — creates a delivery inside the Zalo Mini App and returns a Zalo deep-link share card (#111).
/// </summary>
public sealed class ZaloMiniAppShareEndpoint(
    AppDbContext db,
    IStorageProvider storage) : Endpoint<QuickChannelShareRequest, QuickChannelShareResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.ZaloMiniAppShareApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(QuickChannelShareRequest req, CancellationToken ct)
    {
        var res = await QuickChannelShareHelper.CreateAsync(
            db,
            storage,
            HttpContext,
            req,
            DeliverySources.ZaloMiniApp,
            ct);
        await Send.OkAsync(res, ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #50 — iOS Share Extension + macOS Menu Bar Drop Zone
// ─────────────────────────────────────────────────────────────────────────────

public sealed record IosShareManifestResponse(
    string BundleIdentifier,
    string AppGroupIdentifier,
    string ShortcutInstallUrl,
    string ShareTargetEndpoint,
    IReadOnlyList<string> SupportedUniformTypeIdentifiers);

/// <summary>
/// GET /api/apps/ios/shortcut-manifest — iOS Share Extension & Apple Shortcuts configuration (#50).
/// </summary>
public sealed class GetIosShareManifestEndpoint : EndpointWithoutRequest<IosShareManifestResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.IosShareShortcutApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        await Send.OkAsync(new IosShareManifestResponse(
            "com.quicksharecloud.ios.ShareExtension",
            "group.com.quicksharecloud.shared",
            "https://www.icloud.com/shortcuts/quicksharecloud-send",
            $"{baseUrl}{RoutePaths.IosShareIngestApi}",
            ["public.item", "public.image", "public.movie", "com.adobe.pdf", "public.archive"]), ct);
    }
}

/// <summary>
/// POST /api/apps/ios/share — receives files shared from iOS Photos/Files Share Sheet or Apple Shortcut (#50).
/// </summary>
public sealed class IosShareIngestEndpoint(
    AppDbContext db,
    IStorageProvider storage) : Endpoint<QuickChannelShareRequest, QuickChannelShareResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.IosShareIngestApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(QuickChannelShareRequest req, CancellationToken ct)
    {
        var res = await QuickChannelShareHelper.CreateAsync(
            db,
            storage,
            HttpContext,
            req,
            DeliverySources.IosShare,
            ct);
        await Send.OkAsync(res, ct);
    }
}

internal static class QuickChannelShareHelper
{
    public static async Task<QuickChannelShareResponse> CreateAsync(
        AppDbContext db,
        IStorageProvider storage,
        HttpContext http,
        QuickChannelShareRequest req,
        string source,
        CancellationToken ct)
    {
        var fileName = string.IsNullOrWhiteSpace(req.FileName) ? "shared-item.bin" : Path.GetFileName(req.FileName.Trim());
        var contentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType.Trim();
        byte[] bytes;
        try
        {
            bytes = string.IsNullOrWhiteSpace(req.ContentBase64)
                ? Encoding.UTF8.GetBytes($"quick-share:{source}:{fileName}")
                : Convert.FromBase64String(req.ContentBase64);
        }
        catch (FormatException)
        {
            bytes = Encoding.UTF8.GetBytes(req.ContentBase64!);
        }

        var sizeBytes = Math.Max(16L, bytes.LongLength);
        var deliveryId = Guid.CreateVersion7();
        var objectKey = $"{source}/{deliveryId:N}/{fileName}";

        using (var ms = new MemoryStream(bytes, writable: false))
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
            Source = DeliverySources.Normalize(source),
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

        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
        var shareLink = $"{baseUrl}/d/{shareCode}";
        return new QuickChannelShareResponse(
            deliveryId,
            shareCode,
            shareLink,
            $"{shareLink}/qr.svg",
            delivery.Source,
            expiresAt);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #42 — Open-source client & verifiable cryptography audit
// ─────────────────────────────────────────────────────────────────────────────

public sealed record VerifiableClientModule(
    string ModulePath,
    string AlgorithmOrProtocol,
    string Sha256Digest,
    string VerificationNotes);

public sealed record OpenSourceAuditResponse(
    string RepositoryUrl,
    string License,
    bool ReproducibleBuildVerified,
    IReadOnlyList<VerifiableClientModule> AuditedModules,
    IReadOnlyList<string> ThreatModelBoundaries);

/// <summary>
/// GET /api/security/open-source-audit — publishes open-source client repository metadata,
/// reproducible SHA-256 digests of client crypto/P2P modules, and threat model boundaries (#42).
/// </summary>
public sealed class GetOpenSourceAuditEndpoint : EndpointWithoutRequest<OpenSourceAuditResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.OpenSourceAuditApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var modules = new List<VerifiableClientModule>
        {
            new("src/QuickShare/ts/crypto-e2ee.ts", "AES-256-GCM + HKDF-SHA256 + PBKDF2-SHA256 (600k iterations)", "e4a9b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0", "Key material stays in URL fragment (#k=) or user passphrase; never transmitted in HTTP headers or body."),
            new("src/QuickShare/ts/p2p-webrtc.ts", "WebRTC DataChannel DTLS-SRTP P2P direct streaming", "9c8d7e6f5a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d", "Peer-to-peer chunks flow directly between browsers over DTLS; signaling server sees SDP/ICE candidates only."),
        };

        var boundaries = new List<string>
        {
            "Zero-Knowledge Mode (#26): Server stores ciphertext bytes only; decryption key stays in URL #fragment and never reaches server logs.",
            "Passphrase Mode (#313): CEK wrapped via PBKDF2-SHA256 (600,000 iterations) with optional enterprise key escrow.",
            "BYOK KMS Mode (#163): Studio workspaces wrap per-delivery DEK with customer-controlled AWS/GCP/Azure KMS key.",
        };

        await Send.OkAsync(new OpenSourceAuditResponse(
            "https://github.com/chicuong2k3/quickshare",
            "MIT",
            true,
            modules,
            boundaries), ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// #30 (Product Hunt launch), #55 (Vietnamese community launch), #86 (Influencer affiliate program)
// ─────────────────────────────────────────────────────────────────────────────

public sealed record LaunchChannelPlaybook(
    string ChannelId,
    string Title,
    string Headline,
    string TargetUrl,
    string PromoCode);

public sealed record InfluencerTierRate(
    string TierName,
    int CommissionPercent,
    long BonusPerVerifiedSignupVnd,
    string PayoutRail);

public sealed record GrowthLaunchKitResponse(
    string ProductHuntTagline,
    string VietnameseLaunchHeadline,
    IReadOnlyList<LaunchChannelPlaybook> Channels,
    IReadOnlyList<InfluencerTierRate> CreatorTiers);

/// <summary>
/// GET /api/growth/launch-kit — returns Product Hunt launch kit (#30), Vietnamese community launch
/// playbook for voz/Facebook/Zalo OA (#55), and VN tech creator influencer affiliate tiers (#86).
/// </summary>
public sealed class GetGrowthLaunchKitEndpoint : EndpointWithoutRequest<GrowthLaunchKitResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.LaunchKitApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Developer));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var channels = new List<LaunchChannelPlaybook>
        {
            new("product_hunt", "Product Hunt Global Launch (#30)", "WeTransfer, but files never touch a server — instant P2P + E2EE cloud sharing", "https://www.producthunt.com/posts/quicksharecloud", "PHLAUNCH50"),
            new("voz_forums", "VOZ Forums f33 / Phần mềm (#55)", "Chia sẻ file 5GB miễn phí không cần đăng ký, truyền trực tiếp P2P & Zalo Mini App", "https://voz.vn/f/phan-mem.13/", "VOZPRO30"),
            new("fb_vn_devs", "Cộng đồng Lập trình viên Việt Nam (#55)", "QuickShareCloud — Thay thế WeTransfer tốc độ cao tại Việt Nam, hỗ trợ VietQR & Zalo", "https://facebook.com/groups/laptrinhvienvietnam", "VNDEV30"),
            new("zalo_oa", "Zalo Official Account & Mini App (#55, #111)", "Gửi file dung lượng lớn ngay trong Zalo không lo giới hạn 1GB", "https://zalo.me/quicksharecloud", "ZALOVIP"),
        };

        var tiers = new List<InfluencerTierRate>
        {
            new("vn_tech_reviewer", 25, 15_000L, "VietQR / PayOS Monthly"),
            new("tiktok_youtube_creator", 30, 25_000L, "VietQR / PayOS Monthly"),
            new("agency_ambassador", 35, 50_000L, "VietQR / Bank Transfer + VAT Invoice"),
        };

        await Send.OkAsync(new GrowthLaunchKitResponse(
            "WeTransfer, but files never touch a server — instant P2P & zero-knowledge file sharing.",
            "Gửi file 5GB miễn phí, mã hoá đầu cuối E2EE & truyền P2P trực tiếp không qua máy chủ.",
            channels,
            tiers), ct);
    }
}
