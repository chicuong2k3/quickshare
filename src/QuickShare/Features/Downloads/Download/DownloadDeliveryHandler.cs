using QuickShare.Infrastructure.Caching;
using QuickShare.Features.Deliveries.Otp;

namespace QuickShare.Features.Downloads.Download;

public record DownloadDeliveryQuery(string ShareCode, string? Password, string? Otp);

public sealed class DownloadDeliveryHandler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan FullDownloadTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan RangeRequestTtl = TimeSpan.FromHours(1);
    private const int BurnGraceMinutes = 10;

    public async Task HandleAsync(DownloadDeliveryQuery query, AppDbContext db, OtpLockoutService lockout, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext!;
        var response = httpContext.Response;
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null)
        {
            response.StatusCode = 404;
            return;
        }

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
        {
            response.StatusCode = StatusCodes.Status410Gone;
            return;
        }

        if (delivery.Status == DeliveryStatus.Scheduled && delivery.DeliverAt > DateTime.UtcNow)
        {
            response.StatusCode = StatusCodes.Status423Locked;
            return;
        }

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
        {
            response.StatusCode = 410;
            return;
        }


        // Password gate (issue #44) runs before the OTP gate — the recipient
        // unlocks the password first, then any sender-set code.
        if (!string.IsNullOrEmpty(delivery.PasswordHash))
        {
            var isLocked = await lockout.IsLockedAsync(db, delivery.Id, ct);
            if (isLocked)
            {
                var retryAfter = await lockout.RetryAfterSecondsAsync(db, delivery.Id, ct);
                response.StatusCode = StatusCodes.Status423Locked;
                response.Headers[HttpHeaders.RetryAfter] = retryAfter.ToString(CultureInfo.InvariantCulture);
                return;
            }

            if (string.IsNullOrEmpty(query.Password) || !BCrypt.Net.BCrypt.Verify(query.Password, delivery.PasswordHash))
            {
                await lockout.RecordFailureAsync(db, delivery.Id, ct);
                response.StatusCode = 403;
                return;
            }

            await lockout.ResetAsync(db, delivery.Id, ct);
        }
        if (!string.IsNullOrEmpty(delivery.OtpHash))
        {
            var isLocked = await lockout.IsLockedAsync(db, delivery.Id, ct);
            if (isLocked)
            {
                var retryAfter = await lockout.RetryAfterSecondsAsync(db, delivery.Id, ct);
                response.StatusCode = StatusCodes.Status423Locked;
                response.Headers[HttpHeaders.RetryAfter] = retryAfter.ToString(CultureInfo.InvariantCulture);
                return;
            }

            if (string.IsNullOrEmpty(query.Otp) || !BCrypt.Net.BCrypt.Verify(query.Otp, delivery.OtpHash))
            {
                await lockout.RecordFailureAsync(db, delivery.Id, ct);
                response.StatusCode = 403;
                return;
            }

            await lockout.ResetAsync(db, delivery.Id, ct);
        }

        if (delivery.DownloadCount >= delivery.MaxDownloads)
        {
            // Burn-after-download grace (issue #12): a network drop mid-transfer must not
            // burn the single download — the same IP may retry within a short window.
            var withinBurnGrace = delivery.MaxDownloads == 1
                && await IsWithinBurnGraceAsync(db, delivery.Id, remoteIp, ct);
            if (!withinBurnGrace)
            {
                response.StatusCode = 403;
                return;
            }
        }
        if (string.IsNullOrEmpty(delivery.StoragePath))
        {
            response.StatusCode = 500;
            return;
        }

        var hasRangeHeader = httpContext.Request.Headers.ContainsKey(HttpHeaders.Range);

        // Per-link daily cap (issue #227): count-only check against today's records —
        // no extra identity logging. Range requests resume a download already granted,
        // so they bypass the cap the same way they bypass the counter.
        if (delivery.DailyDownloadLimit is { } dailyLimit && !hasRangeHeader)
        {
            var todayStart = DateTime.UtcNow.Date;
            var todayCount = await db.DownloadTrackings
                .AsNoTracking()
                .CountAsync(t => t.DeliveryId == delivery.Id && t.DownloadedAt >= todayStart, ct);
            if (todayCount >= dailyLimit)
            {
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
        }
        var ttl = hasRangeHeader ? RangeRequestTtl : FullDownloadTtl;

        if (delivery.ExpiresAt.HasValue)
        {
            var remaining = delivery.ExpiresAt.Value - DateTime.UtcNow;
            if (remaining < ttl)
                ttl = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMinutes(5);
        }

        Uri presignedUrl;
        try
        {
            presignedUrl = await storage.GetPresignedDownloadUrlAsync(delivery.StoragePath, ttl, ct);
        }
        catch
        {
            response.StatusCode = 502;
            return;
        }

        response.Redirect(presignedUrl.AbsoluteUri, permanent: false);

        if (!hasRangeHeader)
        {
            // Capture root-scoped services/values now: the request scope (and HttpContext)
            // are disposed once the response completes, racing the background task.
            var scopeFactory = httpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
            var analyticsContext = AnalyticsService.CaptureContext(httpContext);

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var updateDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var entity = await updateDb.Deliveries.FindAsync([delivery.Id]);
                    if (entity is not null)
                    {
                        entity.DownloadCount++;
                        entity.FirstDownloadedAt ??= DateTime.UtcNow;

                        updateDb.DownloadTrackings.Add(new DownloadTracking
                        {
                            Id = Guid.CreateVersion7(),
                            DeliveryId = delivery.Id,
                            IpAddress = remoteIp,
                            DownloadedAt = DateTime.UtcNow
                        });

                        await updateDb.SaveChangesAsync(CancellationToken.None);

                        var analytics = scope.ServiceProvider.GetRequiredService<AnalyticsService>();
                        await analytics.RecordAsync(AnalyticsEvents.DownloadCompleted,
                            new AnalyticsEventData(entity.FileName, entity.FileSize, entity.ContentType, null),
                            analyticsContext, CancellationToken.None);

                        var cache = scope.ServiceProvider.GetRequiredService<CacheService>();
                        await cache.RemoveAsync($"download:info:{query.ShareCode}");
                    }
                }
                catch { /* counter update failure shouldn't break the download */ }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// True when this delivery has a download record from the same IP within the
    /// burn-after-download grace window — i.e. the recipient is retrying a
    /// download that already started, not consuming a fresh one.
    /// </summary>
    private static async Task<bool> IsWithinBurnGraceAsync(AppDbContext db, Guid deliveryId, string remoteIp, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-BurnGraceMinutes);
        return await db.DownloadTrackings
            .AsNoTracking()
            .AnyAsync(t => t.DeliveryId == deliveryId
                && t.IpAddress == remoteIp
                && t.DownloadedAt >= cutoff, ct);
    }
}
