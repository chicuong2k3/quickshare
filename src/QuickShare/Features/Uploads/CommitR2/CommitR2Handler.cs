using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Email;
using System.Security.Cryptography;


namespace QuickShare.Features.Uploads.CommitR2;

public record CommitR2Command(Guid DeliveryId, string UploadId, string ObjectKey, string FileName, string ContentType, long FileSize, IReadOnlyList<R2PartEtag> Parts, string? Otp, string? Password, string? Message, string? Source, DateTime? DeliverAt, double? ExpiryHours, string? Sha256, bool BurnAfterDownload, bool AllowExtensionRequests, IReadOnlyList<string>? RecipientEmails, bool PrivacyMode, bool EmailOtpEnabled)
    : IRequest<Result<CommitR2Response>>;

public record CommitR2Response(string ShareCode, string ShareLink, DateTime ExpiresAt);

public sealed class CommitR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    EmailSender emailSender,
    SenderEmailTokens senderEmailTokens,
    SenderEmailRateLimiter senderEmailRateLimiter,
    IOptions<EmailOptions> emailOptions,
    AnalyticsService analytics,
    ILogger<CommitR2Handler> logger,
    AppDbContext db,
    CacheService cache)
    : IRequestHandler<CommitR2Command, Result<CommitR2Response>>
{
    // Client-side AES-GCM adds a 12-byte nonce + 16-byte tag on top of the declared plain size.
    private const long EncryptionOverheadSlack = 1024;
    private const string LinkEmailSubject = "Link tải file của bạn trên QuickShare";
    private const int FreeRecipientLimit = 3;
    private const int PaidRecipientLimit = 20;

    internal static int RecipientLimit(string plan) =>
        plan is PlanTierLimits.Pro or PlanTierLimits.Studio ? PaidRecipientLimit : FreeRecipientLimit;

    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async ValueTask<Result<CommitR2Response>> Handle(CommitR2Command cmd, CancellationToken cancellationToken)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, cancellationToken);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;

        // Scheduled send — Pro/Studio only. Reject before touching R2 so a disallowed
        // schedule never leaves an orphaned object behind.
        if (cmd.DeliverAt.HasValue)
        {
            if (plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.SchedulingRequiresPaidPlan));
            if (cmd.DeliverAt.Value <= DateTime.UtcNow)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.DeliverAtMustBeFuture));
        }

        // Multi-recipient share (issue #37): the link can be emailed to N people, capped per plan.
        var recipients = NormalizeRecipients(cmd.RecipientEmails);
        if (recipients.Count > RecipientLimit(plan))
            return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.RecipientLimitExceeded));

        // The commit must match the session created by start-r2, for the same owner —
        // otherwise anyone who learns a DeliveryId could hijack or finalize the upload.
        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], cancellationToken);
        if (session is null ||
            session.Status != UploadSessionStatus.InProgress ||
            session.R2UploadId != cmd.UploadId ||
            session.StoragePath != cmd.ObjectKey ||
            session.SenderId != user?.Id)
        {
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                "No matching in-progress upload session"));
        }

        // File-request submissions are capped by the request budget, never the submitter
        // plan or monthly quota — the owner pre-paid for these with their own limits.
        var requestId = session.FileRequestId;
        var quota = user is null || requestId is not null ? null : new QuotaService(db);

        CompletionResult completionResult;
        try
        {
            completionResult = await storage.CompleteR2MultipartUploadAsync(
                cmd.UploadId, cmd.ObjectKey, cmd.Parts, cancellationToken);
        }
        catch (R2StorageException ex)
        {
            logger.LogError(ex, "R2 commit failed for delivery {DeliveryId}", cmd.DeliveryId);
            return Result.Failure<CommitR2Response>(new LightResults.Error(UploadErrors.R2Upstream + "commit failed"));
        }

        if (!completionResult.Success)
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                completionResult.ErrorMessage ?? "R2 commit returned failure"));

        // Enforce the size cap on what actually landed in storage, not the declared size —
        // the declared size is client-controlled and part uploads are not size-constrained.
        long actualSize = session.FileSize;
        if (requestId is { } fileRequestId)
        {
            var stored = await storage.GetObjectSizeAsync(cmd.ObjectKey, cancellationToken);
            if (stored >= 0)
                actualSize = stored;

            var overBudget = await ExceedsRequestBudgetAsync(fileRequestId, actualSize, cancellationToken);
            if (overBudget)
            {
                try
                {
                    await storage.DeleteObjectsAsync(cmd.ObjectKey, cancellationToken);
                }
                catch { /* best-effort cleanup; the sweeper handles leftovers */ }
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.FileRequestLimitReached));
            }
        }
        else
        {
            var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
            if (maxFileSize.HasValue)
            {
                var stored = await storage.GetObjectSizeAsync(cmd.ObjectKey, cancellationToken);
                if (stored >= 0)
                    actualSize = stored;

                if (stored > maxFileSize.Value + EncryptionOverheadSlack)
                {
                    try
                    {
                        await storage.DeleteObjectsAsync(cmd.ObjectKey, cancellationToken);
                    }
                    catch { /* best-effort cleanup; the sweeper handles leftovers */ }
                    return Result.Failure<CommitR2Response>(new LightResults.Error(
                        $"Uploaded size {stored} exceeds {plan} plan limit of {maxFileSize} bytes"));
                }
            }
        }

        // Monthly quota: check the actual committed size, then record on success.
        if (quota is not null)
        {
            var check = await quota.CheckAsync(user!.Id, plan, actualSize, cancellationToken);
            if (check.IsExceeded)
            {
                try
                {
                    await storage.DeleteObjectsAsync(cmd.ObjectKey, cancellationToken);
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

        var sha256 = NormalizeSha256(cmd.Sha256);
        if (sha256 is not null && (sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigit(c))))
            return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.Sha256Invalid));

        var shareCode = ShareCodes.Generate();

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
            SenderId = requestId is null ? user?.Id : null,
            SenderEmail = session.SenderEmail,
            SenderEmailConsent = !string.IsNullOrWhiteSpace(session.SenderEmail),
            // Metadata comes from the validated session, not the commit request.
            FileName = session.FileName,
            ContentType = session.ContentType,
            FileSize = session.FileSize,
            TotalBytes = session.FileSize,
            ChunkCount = cmd.Parts.Count,
            ShareCode = shareCode,
            MaxDownloads = cmd.BurnAfterDownload ? 1 : PlanTierLimits.GetMaxDownloads(plan),
            StoragePath = cmd.ObjectKey,
            ExpiresAt = expiresAt,
            DeliverAt = cmd.DeliverAt,
            Status = cmd.DeliverAt.HasValue ? DeliveryStatus.Scheduled : DeliveryStatus.Active,
            OtpHash = !string.IsNullOrWhiteSpace(cmd.Otp) ? BCrypt.Net.BCrypt.HashPassword(cmd.Otp) : null,
            PasswordHash = password is not null ? BCrypt.Net.BCrypt.HashPassword(password) : null,
            Message = message,
            Sha256 = sha256,
            Source = DeliverySources.Normalize(cmd.Source),
            FileRequestId = requestId,
            AllowExtensionRequests = cmd.AllowExtensionRequests,
            PrivacyMode = cmd.PrivacyMode,
            EmailOtpEnabled = cmd.EmailOtpEnabled,
        };

        session.Status = UploadSessionStatus.Committed;
        session.R2UploadId = null;

        if (quota is not null)
            await quota.RecordAsync(user!.Id, plan, actualSize, cancellationToken);

        if (user is not null && requestId is null)
            await ReferralService.ReleaseOnFirstUploadAsync(user, db, cancellationToken);

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken);

        // The request info is cached — a commit just changed its remaining budget.
        if (requestId is { } committedRequestId)
        {
            var requestCode = await db.FileRequests
                .AsNoTracking()
                .Where(r => r.Id == committedRequestId)
                .Select(r => r.Code)
                .FirstOrDefaultAsync(cancellationToken);
            if (requestCode is not null)
                await cache.RemoveAsync($"file-request:info:{requestCode}", cancellationToken);
        }

        await analytics.RecordAsync(AnalyticsEvents.UploadCompleted,
            new AnalyticsEventData(delivery.FileName, delivery.FileSize, delivery.ContentType, plan), cancellationToken);

        if (!string.IsNullOrWhiteSpace(delivery.SenderEmail) && emailSender.IsConfigured)
        {
            await SendLinkEmailAsync(delivery, shareCode, cancellationToken);
        }

        if (recipients.Count > 0 && emailSender.IsConfigured)
        {
            await SendRecipientEmailsAsync(delivery, shareCode, recipients, cancellationToken);
        }

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        var shareLink = $"{baseUrl}/d/{shareCode}";
        if (!string.IsNullOrEmpty(user?.ReferralCode))
            shareLink += $"?{ReferralPolicy.QueryParam}={user.ReferralCode}";

        return Result.Success(new CommitR2Response(shareCode, shareLink, expiresAt));
    }

    private async Task<bool> ExceedsRequestBudgetAsync(Guid requestId, long actualSize, CancellationToken cancellationToken)
    {
        var request = await db.FileRequests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new
            {
                r.MaxTotalBytes,
                r.MaxFiles,
                CurrentTotalBytes = r.Deliveries
                    .Where(d => d.Status == DeliveryStatus.Active)
                    .Sum(d => (long?)d.TotalBytes) ?? 0,
                CurrentFileCount = r.Deliveries.Count(d => d.Status == DeliveryStatus.Active),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
            return true;

        return request.CurrentTotalBytes + actualSize > request.MaxTotalBytes ||
               request.CurrentFileCount >= request.MaxFiles;
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
    /// Lowercases and trims the client-computed SHA-256 of the plaintext file
    /// (computed before client-side encryption). Null when absent.
    /// </summary>
    private static string? NormalizeSha256(string? sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            return null;

        return sha256.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Emails the anonymous sender their share link, with a single-use confirm link and an
    /// unsubscribe link. Best-effort: a send failure or rate-limit hit must not fail the upload.
    /// </summary>
    private async Task SendLinkEmailAsync(Delivery delivery, string shareCode, CancellationToken cancellationToken)
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

            await emailSender.SendAsync(delivery.SenderEmail!, "QuickShare", LinkEmailSubject, body, cancellationToken);
            logger.LogInformation("Sent sender link email to {Email} for delivery {DeliveryId}", delivery.SenderEmail, delivery.Id);
        }
        catch (Exception ex)
        {
            // ponytail: log and drop — a failed sender email isn't worth a retry queue.
            logger.LogWarning(ex, "Failed to send sender link email to {Email} for delivery {DeliveryId}", delivery.SenderEmail, delivery.Id);
        }
    }

    internal static List<string> NormalizeRecipients(IReadOnlyList<string>? emails)
    {
        if (emails is null)
            return [];

        return emails
            .Select(e => e.Trim())
            .Where(e => e.Contains('@') && e.Length <= 254)
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private async Task SendRecipientEmailsAsync(Delivery delivery, string shareCode, List<string> recipients, CancellationToken cancellationToken)
    {
        var baseUrl = _emailOptions.BaseUrl.TrimEnd('/');
        var shareUrl = $"{baseUrl}{RoutePaths.DeliveryPage}{shareCode}";

        var body = $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào bạn,</p>
              <p>Có một file được gửi cho bạn qua QuickShare:</p>
              <p><a href="{shareUrl}" style="color:#2563eb">{shareUrl}</a></p>
            </div>
            """;

        foreach (var email in recipients)
        {
            try
            {
                await emailSender.SendAsync(email, "QuickShare", LinkEmailSubject, body, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send recipient link email to {Email} for delivery {DeliveryId}", email, delivery.Id);
            }
        }
    }
}
