using QuickShare.Infrastructure.Caching;
using QuickShare.Features.Deliveries.Otp;
using QuickShare.Features.Downloads.Shared;

namespace QuickShare.Features.Downloads.Download;

public record DownloadDeliveryQuery(string ShareCode, string? Password, string? Otp, string? EmailOtp, string? Email, string? RecipientEmail, bool MarketingConsent) : IRequest;

public sealed class DownloadDeliveryHandler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db,
    OtpLockoutService lockout,
    DeliveryAccessService access)
    : IRequestHandler<DownloadDeliveryQuery>
{
    private static readonly TimeSpan FullDownloadTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan RangeRequestTtl = TimeSpan.FromHours(1);
    private const int BurnGraceMinutes = 10;

    public async ValueTask<Unit> Handle(DownloadDeliveryQuery query, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext!;
        var response = httpContext.Response;
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null || delivery.DeletedAt is not null)
        {
            response.StatusCode = 404;
            return Unit.Value;
        }

        // Held for review (issue #162): 404, matching the info endpoint, so the byte stream
        // never becomes the oracle that tells a recipient the file exists but is awaiting
        // somebody's sign-off.
        if (delivery.Status == DeliveryStatus.PendingApproval)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return Unit.Value;
        }

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
        {
            response.StatusCode = StatusCodes.Status410Gone;
            return Unit.Value;
        }

        if (delivery.Status == DeliveryStatus.Scheduled && delivery.DeliverAt > DateTime.UtcNow)
        {
            response.StatusCode = StatusCodes.Status423Locked;
            return Unit.Value;
        }

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
        {
            response.StatusCode = 410;
            return Unit.Value;
        }

        // Scope control (issues #142 CIDR allowlist, #166 country rules) — AND-scoped with
        // every other gate and evaluated before the password so a caller outside the
        // sender's perimeter never gets a password oracle.
        if (await access.CheckAsync(delivery, cancellationToken) != DeliveryAccessDecision.Allowed)
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return Unit.Value;
        }


        // Password gate (issue #44) runs before the OTP gate — the recipient
        // unlocks the password first, then any sender-set code.
        if (!string.IsNullOrEmpty(delivery.PasswordHash))
        {
            var isLocked = await lockout.IsLockedAsync(db, delivery.Id, cancellationToken);
            if (isLocked)
            {
                var retryAfter = await lockout.RetryAfterSecondsAsync(db, delivery.Id, cancellationToken);
                response.StatusCode = StatusCodes.Status423Locked;
                response.Headers[HttpHeaders.RetryAfter] = retryAfter.ToString(CultureInfo.InvariantCulture);
                return Unit.Value;
            }

            if (string.IsNullOrEmpty(query.Password) || !BCrypt.Net.BCrypt.Verify(query.Password, delivery.PasswordHash))
            {
                await lockout.RecordFailureAsync(db, delivery.Id, cancellationToken);
                response.StatusCode = 403;
                return Unit.Value;
            }

            await lockout.ResetAsync(db, delivery.Id, cancellationToken);
        }
        if (!string.IsNullOrEmpty(delivery.OtpHash))
        {
            var isLocked = await lockout.IsLockedAsync(db, delivery.Id, cancellationToken);
            if (isLocked)
            {
                var retryAfter = await lockout.RetryAfterSecondsAsync(db, delivery.Id, cancellationToken);
                response.StatusCode = StatusCodes.Status423Locked;
                response.Headers[HttpHeaders.RetryAfter] = retryAfter.ToString(CultureInfo.InvariantCulture);
                return Unit.Value;
            }

            if (string.IsNullOrEmpty(query.Otp) || !BCrypt.Net.BCrypt.Verify(query.Otp, delivery.OtpHash))
            {
                await lockout.RecordFailureAsync(db, delivery.Id, cancellationToken);
                response.StatusCode = 403;
                return Unit.Value;
            }

            await lockout.ResetAsync(db, delivery.Id, cancellationToken);
        }

        // Email OTP gate (issue #291): the recipient redeemed a single-use emailed code.
        if (delivery.EmailOtpEnabled)
        {
            if (!VerifyEmailOtp(delivery, query.EmailOtp, query.Email))
            {
                response.StatusCode = 403;
                return Unit.Value;
            }

            // Single-use: consume the code before the redirect.
            await db.Deliveries
                .Where(d => d.Id == delivery.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.EmailOtpUsedAt, DateTime.UtcNow), cancellationToken);
        }

        // Recipient email gate (issue #324): anonymous downloaders surrender an email first.
        if (delivery.RequireRecipientEmail)
        {
            var recipientEmail = query.RecipientEmail?.Trim();
            if (string.IsNullOrEmpty(recipientEmail) || !recipientEmail.Contains('@'))
            {
                response.StatusCode = 403;
                return Unit.Value;
            }
        }

        if (delivery.DownloadCount >= delivery.MaxDownloads)
        {
            // Burn-after-download grace (issue #12): a network drop mid-transfer must not
            // burn the single download — the same IP may retry within a short window.
            var withinBurnGrace = delivery.MaxDownloads == 1
                && await IsWithinBurnGraceAsync(db, delivery.Id, remoteIp, cancellationToken);
            if (!withinBurnGrace)
            {
                response.StatusCode = 403;
                return Unit.Value;
            }
        }
        if (string.IsNullOrEmpty(delivery.StoragePath))
        {
            response.StatusCode = 500;
            return Unit.Value;
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
                .CountAsync(t => t.DeliveryId == delivery.Id && t.DownloadedAt >= todayStart, cancellationToken);
            if (todayCount >= dailyLimit)
            {
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                return Unit.Value;
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
            presignedUrl = await storage.GetPresignedDownloadUrlAsync(delivery.StoragePath, ttl, cancellationToken);
        }
        catch
        {
            response.StatusCode = 502;
            return Unit.Value;
        }

        // Recipient download history (issue #220): capture the signed-in user's
        // opted-in id BEFORE the redirect — the request scope dies on response completion.
        Guid? trackUserId = null;
        if (!hasRangeHeader && httpContext.User.Identity?.IsAuthenticated == true)
        {
            var signedIn = await httpContext.User.GetUserOrDefaultAsync(db, cancellationToken);
            if (signedIn is { TrackMyDownloads: true })
                trackUserId = signedIn.Id;
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

                        // Privacy mode (issue #187): the count still drives max-downloads/burn
                        // enforcement, but nothing is logged — no IP, no analytics event, and no
                        // webhook. A share that keeps no record must not export one either.
                        if (!entity.PrivacyMode)
                        {
                            var webhooks = scope.ServiceProvider
                                .GetRequiredService<QuickShare.Features.Developer.Webhooks.WebhookDispatcher>();

                            await webhooks.EnqueueAsync(entity.SenderId, WebhookEvents.DeliveryDownloaded, new
                            {
                                deliveryId = entity.Id,
                                shareCode = entity.ShareCode,
                                fileName = entity.FileName,
                                downloadCount = entity.DownloadCount,
                            }, CancellationToken.None);

                            updateDb.DownloadTrackings.Add(new DownloadTracking
                            {
                                Id = Guid.CreateVersion7(),
                                DeliveryId = delivery.Id,
                                IpAddress = remoteIp,
                                DownloadedAt = DateTime.UtcNow,
                                UserId = trackUserId,
                                RecipientEmail = delivery.RequireRecipientEmail ? query.RecipientEmail?.Trim() : null,
                                MarketingConsent = delivery.RequireRecipientEmail && query.MarketingConsent,
                            });
                        }

                        await updateDb.SaveChangesAsync(CancellationToken.None);

                        if (!entity.PrivacyMode)
                        {
                            var analytics = scope.ServiceProvider.GetRequiredService<AnalyticsService>();
                            await analytics.RecordAsync(AnalyticsEvents.DownloadCompleted,
                                new AnalyticsEventData(entity.FileName, entity.FileSize, entity.ContentType, null),
                                analyticsContext, CancellationToken.None);
                        }

                        var cache = scope.ServiceProvider.GetRequiredService<CacheService>();
                        await cache.RemoveAsync($"download:info:{query.ShareCode}");
                    }
                }
                catch { /* counter update failure shouldn't break the download */ }
            }, CancellationToken.None);
        }

        return Unit.Value;
    }

    /// <summary>
    /// True when this delivery has a download record from the same IP within the
    /// burn-after-download grace window — i.e. the recipient is retrying a
    /// download that already started, not consuming a fresh one.
    /// </summary>
    private static async Task<bool> IsWithinBurnGraceAsync(AppDbContext db, Guid deliveryId, string remoteIp, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-BurnGraceMinutes);
        return await db.DownloadTrackings
            .AsNoTracking()
            .AnyAsync(t => t.DeliveryId == deliveryId
                && t.IpAddress == remoteIp
                && t.DownloadedAt >= cutoff, cancellationToken);
    }

    private static bool VerifyEmailOtp(Delivery delivery, string? code, string? email)
    {
        if (string.IsNullOrEmpty(delivery.EmailOtpHash) || delivery.EmailOtpExpiresAt is null)
            return false;

        if (delivery.EmailOtpExpiresAt < DateTime.UtcNow)
            return false;

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(email))
            return false;

        if (!string.Equals(email.Trim(), delivery.EmailOtpEmail, StringComparison.OrdinalIgnoreCase))
            return false;

        // Single-use, with a 10-minute grace so a mid-transfer network drop doesn't force
        // a fresh code (mirrors the burn-after-download grace).
        if (delivery.EmailOtpUsedAt is { } usedAt && DateTime.UtcNow - usedAt > TimeSpan.FromMinutes(BurnGraceMinutes))
            return false;

        return BCrypt.Net.BCrypt.Verify(code, delivery.EmailOtpHash);
    }
}
