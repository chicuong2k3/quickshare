using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Email;
using System.Security.Cryptography;


namespace QuickShare.Features.Uploads.CommitR2;

public record CommitR2Command(Guid DeliveryId, string UploadId, string ObjectKey, string FileName, string ContentType, long FileSize, IReadOnlyList<R2PartEtag> Parts, string? Otp, string? Password, string? Message, string? Source, DateTime? DeliverAt, double? ExpiryHours);

public record CommitR2Response(string ShareCode, string ShareLink, DateTime ExpiresAt);

public sealed class CommitR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    EmailSender emailSender,
    SenderEmailTokens senderEmailTokens,
    SenderEmailRateLimiter senderEmailRateLimiter,
    IOptions<EmailOptions> emailOptions,
    AnalyticsService analytics,
    ILogger<CommitR2Handler> logger)
{
    // Client-side AES-GCM adds a 12-byte nonce + 16-byte tag on top of the declared plain size.
    private const long EncryptionOverheadSlack = 1024;
    private const string LinkEmailSubject = "Link tải file của bạn trên QuickShare";

    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task<Result<CommitR2Response>> Handle(CommitR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        var quota = user is null ? null : new QuotaService(db);

        // Scheduled send — Pro/Studio only. Reject before touching R2 so a disallowed
        // schedule never leaves an orphaned object behind.
        if (cmd.DeliverAt.HasValue)
        {
            if (plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.SchedulingRequiresPaidPlan));
            if (cmd.DeliverAt.Value <= DateTime.UtcNow)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.DeliverAtMustBeFuture));
        }

        // The commit must match the session created by start-r2, for the same owner —
        // otherwise anyone who learns a DeliveryId could hijack or finalize the upload.
        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], ct);
        if (session is null ||
            session.Status != UploadSessionStatus.InProgress ||
            session.R2UploadId != cmd.UploadId ||
            session.StoragePath != cmd.ObjectKey ||
            session.SenderId != user?.Id)
        {
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                "No matching in-progress upload session"));
        }

        CompletionResult completionResult;
        try
        {
            completionResult = await storage.CompleteR2MultipartUploadAsync(
                cmd.UploadId, cmd.ObjectKey, cmd.Parts, ct);
        }
        catch (R2StorageException ex)
        {
            logger.LogError(ex, "R2 commit failed for delivery {DeliveryId}", cmd.DeliveryId);
            return Result.Failure<CommitR2Response>(new LightResults.Error(UploadErrors.R2Upstream + "commit failed"));
        }

        if (!completionResult.Success)
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                completionResult.ErrorMessage ?? "R2 commit returned failure"));

        // Enforce the plan limit on what actually landed in storage, not the declared size —
        // the declared size is client-controlled and part uploads are not size-constrained.
        long actualSize = session.FileSize;
        var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
        if (maxFileSize.HasValue)
        {
            var stored = await storage.GetObjectSizeAsync(cmd.ObjectKey, ct);
            if (stored >= 0)
                actualSize = stored;

            if (stored > maxFileSize.Value + EncryptionOverheadSlack)
            {
                try
                {
                    await storage.DeleteObjectsAsync(cmd.ObjectKey, ct);
                }
                catch { /* best-effort cleanup; the sweeper handles leftovers */ }
                return Result.Failure<CommitR2Response>(new LightResults.Error(
                    $"Uploaded size {stored} exceeds {plan} plan limit of {maxFileSize} bytes"));
            }
        }

        // Monthly quota: check the actual committed size, then record on success.
        if (quota is not null)
        {
            var check = await quota.CheckAsync(user!.Id, plan, actualSize, ct);
            if (check.IsExceeded)
            {
                try
                {
                    await storage.DeleteObjectsAsync(cmd.ObjectKey, ct);
                }
                catch { /* best-effort cleanup; the sweeper handles leftovers */ }
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.MonthlyQuotaExceeded));
            }
        }

        var message = NormalizeMessage(cmd.Message);
        if (message is not null && message.Length > Delivery.MessageMaxLength)
            return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.MessageTooLong));

        var password = NormalizePassword(cmd.Password);
        if (password is not null && System.Text.Encoding.UTF8.GetByteCount(password) > Delivery.PasswordMaxLength)
            return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.PasswordTooLong));

        var randomBytes = new byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var shareCode = Convert.ToBase64String(randomBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var maxExpiry = PlanTierLimits.GetMaxExpiry(plan);
        if (cmd.ExpiryHours is { } requestedHours)
        {
            if (requestedHours <= 0)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.ExpiryInvalid));

            if (TimeSpan.FromHours(requestedHours) > maxExpiry)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.ExpiryTooLong));
        }

        var expiresAt = ResolveExpiry(cmd, plan);

        var delivery = new Delivery
        {
            Id = cmd.DeliveryId,
            SenderId = user?.Id,
            SenderEmail = session.SenderEmail,
            SenderEmailConsent = !string.IsNullOrWhiteSpace(session.SenderEmail),
            // Metadata comes from the validated session, not the commit request.
            FileName = session.FileName,
            ContentType = session.ContentType,
            FileSize = session.FileSize,
            TotalBytes = session.FileSize,
            ChunkCount = cmd.Parts.Count,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(plan),
            StoragePath = cmd.ObjectKey,
            ExpiresAt = expiresAt,
            DeliverAt = cmd.DeliverAt,
            Status = cmd.DeliverAt.HasValue ? DeliveryStatus.Scheduled : DeliveryStatus.Active,
            OtpHash = !string.IsNullOrWhiteSpace(cmd.Otp) ? BCrypt.Net.BCrypt.HashPassword(cmd.Otp) : null,
            PasswordHash = password is not null ? BCrypt.Net.BCrypt.HashPassword(password) : null,
            Message = message,
            Source = DeliverySources.Normalize(cmd.Source)
        };

        session.Status = UploadSessionStatus.Committed;
        session.R2UploadId = null;

        if (quota is not null)
            await quota.RecordAsync(user!.Id, plan, actualSize, ct);

        if (user is not null)
            await ReferralService.ReleaseOnFirstUploadAsync(user, db, ct);

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        await analytics.RecordAsync(AnalyticsEvents.UploadCompleted,
            new AnalyticsEventData(delivery.FileName, delivery.FileSize, delivery.ContentType, plan), ct);

        if (!string.IsNullOrWhiteSpace(delivery.SenderEmail) && emailSender.IsConfigured)
        {
            await SendLinkEmailAsync(delivery, shareCode, ct);
        }

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        var shareLink = $"{baseUrl}/d/{shareCode}";
        if (!string.IsNullOrEmpty(user?.ReferralCode))
            shareLink += $"?{ReferralPolicy.QueryParam}={user.ReferralCode}";

        return Result.Success(new CommitR2Response(shareCode, shareLink, expiresAt));
    }

    private static DateTime ResolveExpiry(CommitR2Command cmd, string plan)
    {
        var max = PlanTierLimits.GetMaxExpiry(plan);
        var start = cmd.DeliverAt ?? DateTime.UtcNow;

        if (cmd.ExpiryHours is { } requestedHours)
            return start.Add(TimeSpan.FromHours(requestedHours));

        return start.Add(max);
    }

    /// <summary>
    /// Trims the sender note; whitespace-only input becomes null. The max-length
    /// rejection is a security boundary: the note renders escaped on the download
    /// page and must never be emitted as markup.
    /// </summary>
    private static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        return message.Trim();
    }

    /// <summary>
    /// Trims the share password; whitespace-only input becomes null. The length
    /// bound matches BCrypt's 72-byte limit — a longer input would be silently
    /// truncated, so it is rejected instead.
    /// </summary>
    private static string? NormalizePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return null;

        return password.Trim();
    }

    /// <summary>
    /// Emails the anonymous sender their share link, with a single-use confirm link and an
    /// unsubscribe link. Best-effort: a send failure or rate-limit hit must not fail the upload.
    /// </summary>
    private async Task SendLinkEmailAsync(Delivery delivery, string shareCode, CancellationToken ct)
    {
        try
        {
            var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            if (!senderEmailRateLimiter.TryAllow(delivery.SenderEmail!, ip))
            {
                logger.LogWarning("Sender email rate limit hit for {Email}; skipping link email", delivery.SenderEmail);
                return;
            }

            var baseUrl = _emailOptions.BaseUrl.TrimEnd('/');
            var shareUrl = $"{baseUrl}/d/{shareCode}";
            var confirmUrl = $"{baseUrl}{RoutePaths.ConfirmSenderEmail}/{senderEmailTokens.CreateConfirmToken(delivery.Id)}";
            var unsubscribeUrl = $"{baseUrl}{RoutePaths.UnsubscribeSender}/{senderEmailTokens.CreateUnsubscribeToken(delivery.Id)}";

            var body = $"""
                <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
                  <p>Chào bạn,</p>
                  <p>File bạn gửi qua QuickShare đã sẵn sàng. Link chia sẻ:</p>
                  <p><a href="{shareUrl}" style="color:#2563eb">{shareUrl}</a></p>
                  <p>Xác nhận email của bạn để nhận thông báo khi file được tải: <a href="{confirmUrl}">Xác nhận email</a>.</p>
                  <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
                  <p style="font-size:13px;color:#6b7280">
                    Bạn nhận email này vì đã chọn nhận link và cập nhật khi gửi file.
                    Không muốn nhận nữa? <a href="{unsubscribeUrl}">Huỷ đăng ký</a>.
                  </p>
                </div>
                """;

            await emailSender.SendAsync(delivery.SenderEmail!, "QuickShare", LinkEmailSubject, body, ct);
            logger.LogInformation("Sent sender link email to {Email} for delivery {DeliveryId}", delivery.SenderEmail, delivery.Id);
        }
        catch (Exception ex)
        {
            // ponytail: log and drop — a failed sender email isn't worth a retry queue.
            logger.LogWarning(ex, "Failed to send sender link email to {Email} for delivery {DeliveryId}", delivery.SenderEmail, delivery.Id);
        }
    }
}
