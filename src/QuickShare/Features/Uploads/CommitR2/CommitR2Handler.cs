using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Email;
using System.Security.Cryptography;


namespace QuickShare.Features.Uploads.CommitR2;

public record CommitR2Command(Guid DeliveryId, string UploadId, string ObjectKey, string FileName, string ContentType, long FileSize, IReadOnlyList<R2PartEtag> Parts, string? Otp, string? Source);

public record CommitR2Response(string ShareCode, string ShareLink, DateTime ExpiresAt);

public sealed class CommitR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    EmailSender emailSender,
    SenderEmailTokens senderEmailTokens,
    SenderEmailRateLimiter senderEmailRateLimiter,
    IOptions<EmailOptions> emailOptions,
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

        var randomBytes = new byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var shareCode = Convert.ToBase64String(randomBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var expiresAt = DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(plan));

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
            OtpHash = !string.IsNullOrWhiteSpace(cmd.Otp) ? BCrypt.Net.BCrypt.HashPassword(cmd.Otp) : null,
            Source = DeliverySources.Normalize(cmd.Source)
        };

        session.Status = UploadSessionStatus.Committed;
        session.R2UploadId = null;

        if (quota is not null)
            await quota.RecordAsync(user!.Id, plan, actualSize, ct);

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(delivery.SenderEmail) && emailSender.IsConfigured)
        {
            await SendLinkEmailAsync(delivery, shareCode, ct);
        }

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        return Result.Success(new CommitR2Response(shareCode, $"{baseUrl}/d/{shareCode}", expiresAt));
    }

    /// <summary>
    /// Emails the anonymous sender their share link, with a single-use confirm link and an
    /// unsubscribe link. Best-effort: a send failure or rate-limit hit must not fail the upload.
    /// </summary>
    private async Task SendLinkEmailAsync(Delivery delivery, string shareCode, CancellationToken ct)
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

        try
        {
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
