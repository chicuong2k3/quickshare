using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Channels;
using QuickShare.Features.Workspaces.Domains;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Email;
using System.Security.Cryptography;


namespace QuickShare.Features.Uploads.CommitR2;

public record CommitR2Command(Guid DeliveryId, string UploadId, string ObjectKey, string FileName, string ContentType, long FileSize, IReadOnlyList<R2PartEtag> Parts, string? Otp, string? Password, string? Message, string? Source, DateTime? DeliverAt, double? ExpiryHours, string? Sha256, bool BurnAfterDownload, bool AllowExtensionRequests, IReadOnlyList<string>? RecipientEmails, bool PrivacyMode, bool EmailOtpEnabled, bool RequireRecipientEmail, bool PublicStatsEnabled, string? AllowedIpRanges = null, string? AllowedCountries = null, string? BlockedCountries = null, Guid? FolderId = null, bool CommentsEnabled = false, bool WatermarkEnabled = false, bool OcrEnabled = false, IReadOnlyList<CommitFile>? AdditionalFiles = null)
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
    CacheService cache,
    ChannelNotifier channels,
    WorkspaceDomainReader domains,
    QuickShare.Features.Developer.Webhooks.WebhookDispatcher webhooks)
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

        // Scope rules (issues #142 CIDR allowlist, #166 country rules) are a paid control.
        // Normalize first: an all-garbage list collapses to null, which is "no rule" and
        // must not trip the plan gate.
        // Preview watermark (issue #155) is a paid control, and refused rather than silently
        // dropped: a sender who thinks their preview carries their name and finds out it does
        // not has been told something untrue about who can screenshot their work.
        if (cmd.WatermarkEnabled && plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.WatermarkRequiresPaidPlan));

        var allowedIpRanges = DeliveryAccessPolicy.NormalizeIpRanges(cmd.AllowedIpRanges);
        var allowedCountries = DeliveryAccessPolicy.NormalizeCountries(cmd.AllowedCountries);
        var blockedCountries = DeliveryAccessPolicy.NormalizeCountries(cmd.BlockedCountries);
        var hasAccessRules = allowedIpRanges is not null || allowedCountries is not null || blockedCountries is not null;
        if (hasAccessRules && plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

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

        // Extra files of a bundle (issue #79). Completed after the primary so a bundle failure
        // never leaves a delivery that exists but has no first file; the delivery either carries
        // all of its files or is refused outright.
        var bundle = await BundleCommitter.CompleteAsync(
            cmd.DeliveryId,
            cmd.AdditionalFiles ?? [],
            requestId is null ? PlanTierLimits.GetMaxFileSize(plan) : null,
            storage,
            logger,
            cancellationToken);

        if (bundle.Error is not null)
        {
            // Everything under this delivery's prefix goes, including the primary: a
            // half-committed bundle is worse than none, because the recipient cannot tell.
            try
            {
                await storage.DeleteObjectsAsync(cmd.ObjectKey, cancellationToken);
                foreach (var claimed in cmd.AdditionalFiles ?? [])
                    await storage.DeleteObjectsAsync(claimed.ObjectKey, cancellationToken);
            }
            catch { /* best-effort cleanup; the sweeper handles leftovers */ }

            return Result.Failure<CommitR2Response>(new LightResults.Error(bundle.Error));
        }

        // Quota is charged on the whole bundle. Charging only file #1 would make a hundred-file
        // upload free after the first, which is the obvious way to walk around a monthly cap.
        actualSize += bundle.TotalBytes;

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

        // The uploader's live workspace seat (issue #106), resolved once — null for personal
        // shares and for anonymous uploads.
        var workspaceId = user is null
            ? null
            : await db.WorkspaceMembers
                .AsNoTracking()
                .Where(m => m.UserId == user.Id && m.RevokedAt == null && m.JoinedAt != null)
                .Select(m => (Guid?)m.WorkspaceId)
                .FirstOrDefaultAsync(cancellationToken);

        // Shared folder (issue #151): must belong to the sender's own workspace. A folder id
        // from somewhere else is refused, not quietly dropped — silently filing a share
        // somewhere other than where the sender asked is worse than failing the commit.
        Guid? folderId = null;
        WorkspaceFolder.EffectiveDefaults? folderDefaults = null;
        if (requestId is null && workspaceId is { } folderWorkspaceId && cmd.FolderId is { } requestedFolderId)
        {
            folderDefaults = await FolderDefaults.ResolveAsync(
                db, folderWorkspaceId, requestedFolderId, cancellationToken);

            if (folderDefaults is null)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.FolderNotFound));

            folderId = requestedFolderId;
        }

        // Approval gate (issue #162): the workspace switch, or any folder above this one, and
        // it only ever tightens. Deliberately independent of DefaultsLocked — a review gate the
        // sender can switch off is not a gate.
        var requiresApproval = workspaceId is not null
            && user is not null
            && ((folderDefaults?.RequireApproval ?? false)
                || await db.Workspaces
                    .AsNoTracking()
                    .Where(w => w.Id == workspaceId)
                    .Select(w => w.RequireDeliveryApproval)
                    .FirstOrDefaultAsync(cancellationToken));

        var maxExpiry = PlanTierLimits.GetMaxExpiry(plan);

        // Locked folder defaults are policy, not prefill (issue #245). Unlocked ones never
        // reach here: they are served to the upload form and the sender stays in control.
        if (folderDefaults is { Locked: true })
        {
            if (folderDefaults.PasswordRequired && password is null)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.FolderPasswordRequired));

            if (folderDefaults.ExpiryHours is { } lockedHours)
            {
                // The plan cap still applies first — a folder rule may tighten the ceiling,
                // never raise it, so a locked window longer than the plan allows is clamped
                // rather than turned into an ExpiryTooLong the uploader cannot act on.
                cmd = cmd with { ExpiryHours = Math.Min(lockedHours, maxExpiry.TotalHours) };
            }

            if (folderDefaults.PrivacyMode)
                cmd = cmd with { PrivacyMode = true };
        }

        if (cmd.ExpiryHours is { } requestedHours)
        {
            if (requestedHours <= 0)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.ExpiryInvalid));

            if (TimeSpan.FromHours(requestedHours) > maxExpiry)
                return Result.Failure<CommitR2Response>(new LightResults.Error(ErrorCodes.ExpiryTooLong));
        }

        // Retention policy (issue #161): the sender's own choice is clamped to the
        // account-wide window. Clamping, not rejecting — the upload already landed in R2,
        // and a compliance window is a ceiling, not a validation error.
        var expiresAt = RetentionPolicy.Clamp(ResolveExpiry(cmd, plan), user?.RetentionDays);

        var delivery = new Delivery
        {
            Id = cmd.DeliveryId,
            SenderId = requestId is null ? user?.Id : null,
            // Stamped from the sender's live seat (issue #106) so team-mates see the share in
            // the shared history. A file-request submission has no sender and no workspace.
            WorkspaceId = requestId is null ? workspaceId : null,
            FolderId = folderId,
            // Read back out of the key rather than carried through the request: the key is
            // where the bytes actually went, so this cannot disagree with reality (issue #119).
            StorageRegion = StorageRegions.FromKey(cmd.ObjectKey),
            // Privacy mode wins: a share that keeps no download record should not grow a
            // thread instead (issues #289/#187).
            CommentsEnabled = cmd.CommentsEnabled && !cmd.PrivacyMode,
            WatermarkEnabled = cmd.WatermarkEnabled,
            // Privacy mode wins again (issues #243/#187): a share that keeps no record must not
            // grow a searchable transcript of itself instead. Gated here rather than in the job,
            // so the flag on the row is already the truth.
            OcrRequested = cmd.OcrEnabled && !cmd.PrivacyMode && DeliveryOcrText.IsIndexable(session.ContentType),
            SenderEmail = session.SenderEmail,
            SenderEmailConsent = !string.IsNullOrWhiteSpace(session.SenderEmail),
            // Metadata comes from the validated session, not the commit request.
            FileName = session.FileName,
            ContentType = session.ContentType,
            FileSize = session.FileSize,
            TotalBytes = session.FileSize,
            // The Delivery row keeps describing file #1 exactly as before (issue #79); these two
            // describe the bundle, so nothing written before multi-file has to change meaning.
            FileCount = 1 + bundle.Files.Count,
            TotalFileBytes = actualSize,
            ChunkCount = cmd.Parts.Count,
            ShareCode = shareCode,
            MaxDownloads = cmd.BurnAfterDownload ? 1 : PlanTierLimits.GetMaxDownloads(plan),
            StoragePath = cmd.ObjectKey,
            ExpiresAt = expiresAt,
            DeliverAt = cmd.DeliverAt,
            Status = requiresApproval
                ? DeliveryStatus.PendingApproval
                : cmd.DeliverAt.HasValue ? DeliveryStatus.Scheduled : DeliveryStatus.Active,
            ApprovalRequestedAt = requiresApproval ? DateTime.UtcNow : null,
            // Held, not sent: the point of a gate is that nobody outside learns the link
            // exists until a reviewer clears it (issue #162).
            PendingRecipients = requiresApproval && recipients.Count > 0
                ? string.Join(',', recipients)
                : null,
            OtpHash = !string.IsNullOrWhiteSpace(cmd.Otp) ? BCrypt.Net.BCrypt.HashPassword(cmd.Otp) : null,
            PasswordHash = password is not null ? BCrypt.Net.BCrypt.HashPassword(password) : null,
            Message = message,
            Sha256 = sha256,
            Source = DeliverySources.Normalize(cmd.Source),
            FileRequestId = requestId,
            AllowExtensionRequests = cmd.AllowExtensionRequests,
            PrivacyMode = cmd.PrivacyMode,
            EmailOtpEnabled = cmd.EmailOtpEnabled,
            RequireRecipientEmail = cmd.RequireRecipientEmail,
            PublicStatsEnabled = cmd.PublicStatsEnabled,
            AllowedIpRanges = allowedIpRanges,
            AllowedCountries = allowedCountries,
            BlockedCountries = blockedCountries,
        };

        session.Status = UploadSessionStatus.Committed;
        session.R2UploadId = null;

        if (quota is not null)
            await quota.RecordAsync(user!.Id, plan, actualSize, cancellationToken);

        if (user is not null && requestId is null)
            await ReferralService.ReleaseOnFirstUploadAsync(user, db, cancellationToken);

        db.Deliveries.Add(delivery);

        // Every delivery gets a row for file #1 too, so "at least one DeliveryFile" holds without
        // exception and no reader has to branch on which shape it is looking at.
        db.DeliveryFiles.Add(BundleCommitter.PrimaryRow(delivery, actualSize - bundle.TotalBytes));
        foreach (var file in bundle.Files)
            db.DeliveryFiles.Add(file);

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

        // The sender's own copy of the link waits too (issue #162): a mail saying the file is
        // ready to share, for a link that refuses everyone, is worse than no mail.
        if (!requiresApproval && !string.IsNullOrWhiteSpace(delivery.SenderEmail) && emailSender.IsConfigured)
        {
            await SendLinkEmailAsync(delivery, shareCode, cancellationToken);
        }

        if (!requiresApproval && recipients.Count > 0 && emailSender.IsConfigured)
        {
            await SendRecipientEmailsAsync(delivery, shareCode, recipients, cancellationToken);
        }

        // Announce it in the workspace's chat channel (issue #180). Held shares are announced
        // on approval instead, for the same reason their recipient mails are: a channel post is
        // how somebody outside first learns the link exists.
        if (!requiresApproval)
        {
            await channels.NotifyDeliveryAsync(
                delivery.WorkspaceId,
                delivery.FileName,
                await domains.DeliveryUrlAsync(delivery.WorkspaceId, shareCode, cancellationToken),
                delivery.ExpiresAt,
                cancellationToken);
        }

        // Outbound webhook (issue #39). Queued, not sent — an upload must not wait on somebody
        // else's HTTP endpoint, and a receiver that is down has to be retried rather than lost.
        // Held shares are announced on approval instead, same rule as the chat post above.
        if (!requiresApproval)
        {
            await webhooks.EnqueueAsync(delivery.SenderId, WebhookEvents.DeliveryCreated, new
            {
                deliveryId = delivery.Id,
                shareCode = delivery.ShareCode,
                fileName = delivery.FileName,
                fileCount = delivery.FileCount,
                totalBytes = delivery.TotalFileBytes,
                expiresAt = delivery.ExpiresAt,
            }, cancellationToken);
        }

        // Recent-recipients + address book (issue #316): remember who this user
        // sent to so the upload page can offer quick-pick. Captured only for
        // signed-in users and never in privacy mode — a private share must not
        // leave any recipient breadcrumbs. Best-effort: a failure here must not
        // fail the upload.
        if (user is not null && !cmd.PrivacyMode && recipients.Count > 0)
        {
            await CaptureRecipientsAsync(user.Id, recipients, cancellationToken);
        }

        // The link the sender copies is the workspace's own address when it has one (issue #81):
        // an agency that white-labels its recipient page and then hands out our hostname has
        // white-labelled nothing.
        var shareLink = await domains.DeliveryUrlAsync(delivery.WorkspaceId, shareCode, cancellationToken);
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
            var shareUrl = await domains.DeliveryUrlAsync(delivery.WorkspaceId, shareCode, cancellationToken);
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

    private async Task CaptureRecipientsAsync(Guid userId, List<string> recipients, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var existing = await db.RecipientContacts
                .Where(c => c.UserId == userId && recipients.Contains(c.Email))
                .ToDictionaryAsync(c => c.Email, cancellationToken);

            foreach (var email in recipients)
            {
                if (existing.TryGetValue(email, out var contact))
                {
                    contact.LastUsedAt = now;
                    contact.UseCount++;
                }
                else
                {
                    db.RecipientContacts.Add(new RecipientContact
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = userId,
                        Email = email,
                        LastUsedAt = now,
                        UseCount = 1,
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync($"user:recipients:{userId}", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to capture recipient contacts for user {UserId}", userId);
        }
    }

    private async Task SendRecipientEmailsAsync(Delivery delivery, string shareCode, List<string> recipients, CancellationToken cancellationToken)
    {
        var shareUrl = await domains.DeliveryUrlAsync(delivery.WorkspaceId, shareCode, cancellationToken);

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
