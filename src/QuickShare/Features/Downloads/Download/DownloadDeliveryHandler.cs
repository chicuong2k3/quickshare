using QuickShare.Infrastructure.Caching;
using QuickShare.Features.Deliveries.Otp;

namespace QuickShare.Features.Downloads.Download;

public record DownloadDeliveryQuery(string ShareCode, string? Otp);

public sealed class DownloadDeliveryHandler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan FullDownloadTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan RangeRequestTtl = TimeSpan.FromHours(1);

    public async Task HandleAsync(DownloadDeliveryQuery query, AppDbContext db, OtpLockoutService lockout, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext!;
        var response = httpContext.Response;

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct);

        if (delivery is null)
        {
            response.StatusCode = 404;
            return;
        }

        if (delivery.Status == DeliveryStatus.Suspended)
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
            response.StatusCode = 403;
            return;
        }
        if (string.IsNullOrEmpty(delivery.StoragePath))
        {
            response.StatusCode = 500;
            return;
        }

        var hasRangeHeader = httpContext.Request.Headers.ContainsKey(HttpHeaders.Range);
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
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "";
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
}
