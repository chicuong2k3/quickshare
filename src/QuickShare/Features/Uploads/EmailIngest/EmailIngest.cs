using System.Security.Cryptography;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Uploads.EmailIngest;

/// <summary>
/// One attachment as the mail provider handed it over.
///
/// Base64 rather than a URL on purpose: a URL would mean fetching from an address somebody else
/// controls, which is the SSRF surface `RemoteUrlFetcher` exists to contain, and it would make
/// ingestion depend on that link still being alive.
/// </summary>
public sealed record InboundAttachment(string FileName, string? ContentType, string ContentBase64);

/// <summary>The shape this deployment accepts from its mail provider (issue #104).</summary>
public sealed class InboundEmailRequest
{
    /// <summary>Who sent the mail. Must be an address the account owns, or nothing happens.</summary>
    public string? From { get; set; }

    /// <summary>The ingest address it was sent to, which is what identifies the account.</summary>
    public string? To { get; set; }

    public string? Subject { get; set; }

    public List<InboundAttachment>? Attachments { get; set; }
}

/// <summary>
/// Turns a forwarded mail into deliveries (issue #104).
///
/// The whole feature is "forward it and get a link back": no app, no browser, no account switch
/// on a phone that will not cooperate. What makes it safe to expose is not the address being
/// secret — an address travels in every mail header it ever appears in — but that **only the
/// account's own address may use it**. Anybody who learns the address can send to it; nobody but
/// the owner gets a delivery out of it.
///
/// The alternative, accepting from anyone, turns a private ingest address into an open upload
/// endpoint charged to somebody else's quota.
/// </summary>
public sealed class EmailIngestService(
    AppDbContext db,
    IStorageProvider storage,
    EmailSender emailSender,
    QuickShare.Features.Workspaces.Domains.WorkspaceDomainReader domains,
    ILogger<EmailIngestService> logger)
{
    /// <summary>Local-part prefix, so an ingest address is recognisable in a mail log.</summary>
    public const string AddressPrefix = "u-";
    public const int TokenLength = 20;
    /// <summary>A mail is not a bulk uploader; more than this in one message is a mistake.</summary>
    public const int MaxAttachments = 10;

    /// <summary>The account's own ingest address, minted on first use and stable afterwards.</summary>
    public async Task<string> AddressForAsync(User user, string domain, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(user.EmailIngestToken))
        {
            user.EmailIngestToken = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(TokenLength / 2)).ToLowerInvariant();

            await db.SaveChangesAsync(ct);
        }

        return $"{AddressPrefix}{user.EmailIngestToken}@{domain}";
    }

    /// <summary>Number of deliveries created. Zero is a normal outcome, not an error.</summary>
    public async Task<int> IngestAsync(InboundEmailRequest request, CancellationToken ct)
    {
        var token = ExtractToken(request.To);
        if (token is null)
            return 0;

        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailIngestToken == token, ct);
        if (user is null)
            return 0;

        // The sender must be the owner. Mail From is forgeable, which is exactly why this is a
        // *convenience* gate on an address that only ever produces links for one account: the
        // worst a forged From achieves is a delivery in the owner's own list, and the owner is
        // the only one who is ever told about it.
        var from = ExtractAddress(request.From);
        if (from is null || !string.Equals(from, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Inbound mail for {UserId} refused: sender is not the account", user.Id);
            return 0;
        }

        var attachments = (request.Attachments ?? []).Take(MaxAttachments).ToList();
        if (attachments.Count == 0)
            return 0;

        var links = new List<string>();
        foreach (var attachment in attachments)
        {
            var link = await StoreAsync(user, attachment, ct);
            if (link is not null)
                links.Add(link);
        }

        if (links.Count > 0 && emailSender.IsConfigured)
            await ReplyAsync(user, request.Subject, links, ct);

        return links.Count;
    }

    private async Task<string?> StoreAsync(User user, InboundAttachment attachment, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(attachment.ContentBase64);
        }
        catch (FormatException)
        {
            logger.LogWarning("Inbound attachment for {UserId} was not valid base64", user.Id);
            return null;
        }

        var cap = PlanTierLimits.GetMaxFileSize(user.Plan);
        if (cap is { } max && bytes.LongLength > max)
            return null;

        var quota = new QuotaService(db);
        var check = await quota.CheckAsync(user.Id, user.Plan, bytes.LongLength, ct);
        if (check.IsExceeded)
            return null;

        var deliveryId = Guid.CreateVersion7();
        var name = DeliveryFile.SanitizeName(attachment.FileName, 0);
        var objectKey = R2ObjectKey.For(user.Id, deliveryId, name, user.PreferredStorageRegion);

        try
        {
            using var content = new MemoryStream(bytes);
            await storage.UploadObjectAsync(objectKey, content, attachment.ContentType ?? "application/octet-stream", ct);
        }
        catch (Exception ex) when (ex is R2StorageException or IOException)
        {
            logger.LogError(ex, "Inbound attachment could not be stored for {UserId}", user.Id);
            return null;
        }

        var delivery = new Delivery
        {
            Id = deliveryId,
            SenderId = user.Id,
            FileName = name,
            ContentType = attachment.ContentType ?? "application/octet-stream",
            FileSize = bytes.LongLength,
            TotalBytes = bytes.LongLength,
            TotalFileBytes = bytes.LongLength,
            ChunkCount = 1,
            ShareCode = ShareCodes.Generate(),
            MaxDownloads = PlanTierLimits.GetMaxDownloads(user.Plan),
            StoragePath = objectKey,
            ExpiresAt = RetentionPolicy.Clamp(DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(user.Plan)), user.RetentionDays),
            Status = DeliveryStatus.Active,
            Source = DeliverySources.Email,
            StorageRegion = StorageRegions.FromKey(objectKey),
        };

        db.Deliveries.Add(delivery);
        await quota.RecordAsync(user.Id, user.Plan, bytes.LongLength, ct);
        await db.SaveChangesAsync(ct);

        return await domains.DeliveryUrlAsync(null, delivery.ShareCode, ct);
    }

    private async Task ReplyAsync(User user, string? subject, List<string> links, CancellationToken ct)
    {
        var list = string.Join(string.Empty, links.Select(link =>
            $"<p><a href=\"{link}\" style=\"color:#2563eb\">{link}</a></p>"));

        var body = $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Your forwarded {(links.Count == 1 ? "file is" : "files are")} ready to share:</p>
              {list}
            </div>
            """;

        try
        {
            // Only ever to the account's own address — the reply is the delivery of the links,
            // and sending it anywhere else would make this a way to mail strangers from us.
            await emailSender.SendAsync(user.Email, user.Name,
                string.IsNullOrWhiteSpace(subject) ? "Your QuickShare link" : $"QuickShare: {subject}", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reply to inbound mail for {UserId}", user.Id);
        }
    }

    /// <summary>The token out of <c>u-abc123@inbound.example.com</c>, or null if it is not one of ours.</summary>
    public static string? ExtractToken(string? address)
    {
        var normalized = ExtractAddress(address);
        if (normalized is null)
            return null;

        var at = normalized.IndexOf('@', StringComparison.Ordinal);
        var local = at < 0 ? normalized : normalized[..at];

        if (!local.StartsWith(AddressPrefix, StringComparison.Ordinal))
            return null;

        var token = local[AddressPrefix.Length..];

        return token.Length == TokenLength && token.All(char.IsAsciiLetterOrDigit) ? token : null;
    }

    /// <summary>Strips the display name off <c>Nam &lt;nam@example.com&gt;</c>.</summary>
    public static string? ExtractAddress(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        var open = value.LastIndexOf('<');
        var close = value.LastIndexOf('>');
        if (open >= 0 && close > open)
            value = value[(open + 1)..close];

        value = value.Trim().ToLowerInvariant();

        return value.Contains('@', StringComparison.Ordinal) ? value : null;
    }
}
