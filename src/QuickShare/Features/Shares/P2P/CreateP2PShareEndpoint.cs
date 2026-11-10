using System.Security.Cryptography;

namespace QuickShare.Features.Shares.P2P;

public sealed record CreateP2PShareRequest(string FileName, long FileSize, string MimeType, string BaseUrl);

public sealed record CreateP2PShareResponse(string ShareCode, string ShareLink, string PairingCode);

public sealed record ResolveP2PPairingResponse(string ShareCode, string ShareLink);

public sealed class CreateP2PShareEndpoint : Endpoint<CreateP2PShareRequest>
{
    private static readonly TimeSpan PairingCodeTtl = TimeSpan.FromMinutes(10);

    public override void Configure()
    {
        Post("/api/shares/p2p");
        AllowAnonymous();
        Validator<CreateP2PShareValidator>();
        Description(b => b.WithTags(ApiTags.P2p));
        Summary(s =>
        {
            s.Summary = "Create a P2P share link and 6-digit pairing key (metadata only)";
            s.Description = "Creates a share link and 10-minute 6-digit pairing code (issue #40) for WebRTC P2P file transfer.";
            s.Responses[200] = "Share created. Returns shareCode, shareLink, and pairingCode.";
            s.Responses[429] = "Daily P2P upload limit reached.";
        });
    }

    public override async Task HandleAsync(CreateP2PShareRequest req, CancellationToken ct)
    {
        // Resolve identity: authenticated user or anonymous (IP-tracked).
        var db = Resolve<AppDbContext>();
        var principal = HttpContext.User;
        var isAuthenticated = principal.Identity?.IsAuthenticated == true;

        User? user = null;
        if (isAuthenticated)
        {
            try
            {
                user = await principal.GetUserAsync(db, ct);
            }
            catch (InvalidOperationException)
            {
                // Unknown user claims — treat as anonymous rather than failing the share.
                isAuthenticated = false;
            }
        }

        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Enforce daily P2P upload quota.
        var quota = new P2PQuotaService(db);
        var quotaResult = await quota.CheckAndIncrementAsync(
            isAuthenticated ? user!.Id : null,
            ipAddress,
            plan,
            ct);

        if (!quotaResult.IsAllowed)
        {
            await SendDailyLimitReachedAsync(quotaResult, isAuthenticated && plan == PlanTierLimits.Free, ct);
            return;
        }

        var shareCode = GenerateShareCode();

        // Ensure uniqueness
        while (await db.Deliveries.AnyAsync(d => d.ShareCode == shareCode, ct))
        {
            shareCode = GenerateShareCode();
        }

        var now = DateTime.UtcNow;
        var pairingCode = await GeneratePairingCodeAsync(db, now, ct);

        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            ShareCode = shareCode,
            Alias = pairingCode,
            FileName = req.FileName,
            ContentType = req.MimeType,
            FileSize = req.FileSize,
            TotalBytes = req.FileSize,
            MaxDownloads = 0,
            DownloadCount = 0,
            ExpiresAt = now.Add(PairingCodeTtl),
            SenderId = isAuthenticated ? user!.Id : null
        };

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        var analytics = Resolve<AnalyticsService>();
        await analytics.RecordAsync(AnalyticsEvents.P2pSessionCreated,
            new AnalyticsEventData(req.FileName, req.FileSize, req.MimeType, plan), ct);

        var shareLink = $"{req.BaseUrl.TrimEnd('/')}/p2p/d/{shareCode}";

        HttpContext.Response.Headers.Location = $"/p2p/d/{shareCode}";
        await Send.ResponseAsync(new CreateP2PShareResponse(shareCode, shareLink, pairingCode), StatusCodes.Status200OK, ct);
    }

    private async Task SendDailyLimitReachedAsync(P2PQuotaResult quota, bool canUnlockWithAd, CancellationToken ct)
    {
        var payload = new
        {
            error = "daily_p2p_upload_limit_reached",
            message = $"Daily P2P upload limit reached ({quota.Used}/{quota.Allowance}). " +
                      (canUnlockWithAd
                          ? "Watch an ad to unlock 1 more upload, or upgrade to Pro for unlimited."
                          : "Sign in or upgrade to Pro for unlimited uploads."),
            limit = quota.Limit,
            used = quota.Used,
            unlocked = quota.Unlocked,
            allowance = quota.Allowance,
            remaining = quota.Remaining,
            canUnlockWithAd
        };

        await Send.ResponseAsync(payload, StatusCodes.Status429TooManyRequests, ct);
    }

    private static string GenerateShareCode()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    private static async Task<string> GeneratePairingCodeAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var existing = await db.Deliveries.FirstOrDefaultAsync(d => d.Alias == code, ct);
            if (existing is null)
            {
                return code;
            }

            if (existing.ExpiresAt is { } exp && exp < now)
            {
                existing.Alias = null;
                return code;
            }
        }

        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// GET /api/shares/p2p/pair/{code} — resolves a 6-digit pairing key to a P2P session (issue #40).
/// One-time use with a 10-minute timeout.
/// </summary>
public sealed class ResolveP2PPairingEndpoint : EndpointWithoutRequest<ResolveP2PPairingResponse>
{
    public override void Configure()
    {
        Get($"{RoutePaths.P2pPairRoute}/{{code}}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.P2p));
        Summary(s =>
        {
            s.Summary = "Resolve a 6-digit P2P pairing code";
            s.Description = "Issue #40. Consumes the 6-digit pairing code (one-time use, 10-min TTL) and returns the P2P shareCode.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = (Route<string>("code") ?? string.Empty).Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<AppDbContext>();
        var now = DateTime.UtcNow;
        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Alias == code && d.DeletedAt == null, ct);

        if (delivery is null || (delivery.ExpiresAt is { } exp && exp < now))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // One-time use: clear the pairing alias once claimed by a receiver.
        delivery.Alias = null;
        await db.SaveChangesAsync(ct);

        var shareLink = $"/p2p/d/{delivery.ShareCode}";
        await Send.OkAsync(new ResolveP2PPairingResponse(delivery.ShareCode, shareLink), ct);
    }
}
