using System.Security.Cryptography;
using System.Text.Json;
using QuickShare.Extensions;
using QuickShare.Infrastructure;

namespace QuickShare.Features.Shares.P2P;

public sealed record CreateP2PShareRequest(string FileName, long FileSize, string MimeType, bool? HasPin, string BaseUrl);

public sealed record CreateP2PShareResponse(string ShareCode, string ShareLink);

public sealed class CreateP2PShareEndpoint : Endpoint<CreateP2PShareRequest>
{
    public override void Configure()
    {
        Post("/api/shares/p2p");
        AllowAnonymous();
        Validator<CreateP2PShareValidator>();
        Description(b => b.WithTags(ApiTags.P2p));
        Summary(s =>
        {
            s.Summary = "Create a P2P share link (metadata only)";
            s.Description = "Creates a share link for WebRTC P2P file transfer. No file upload required. Free/anonymous users are limited to a daily quota of share links; paid plans are unlimited.";
            s.Responses[201] = "Share created. Returns shareCode and shareLink.";
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

        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            ShareCode = shareCode,
            FileName = req.FileName,
            ContentType = req.MimeType,
            FileSize = req.FileSize,
            TotalBytes = req.FileSize,
            MaxDownloads = 0,
            DownloadCount = 0,
            EgressBytes = 0,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            SenderId = isAuthenticated ? user!.Id : null
        };

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        var shareLink = $"{req.BaseUrl.TrimEnd('/')}/p2p/d/{shareCode}";

        HttpContext.Response.Headers.Location = $"/p2p/d/{shareCode}";
        await Send.ResponseAsync(new CreateP2PShareResponse(shareCode, shareLink), StatusCodes.Status200OK, ct);
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
}
