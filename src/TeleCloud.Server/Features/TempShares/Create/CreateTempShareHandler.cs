using LightResults;
using Microsoft.Extensions.Options;
using TL;

namespace TeleCloud.Server.Features.TempShares.Create;

public record CreateTempShareCommand(
    byte[] Content,
    string Filename,
    string ContentType,
    TimeSpan Expiry,
    string SchemeHost);

public sealed class CreateTempShareHandler(
    TelegramService telegram,
    IOptions<QuickShareOptions> options)
{
    public async Task<Result<TempShareResponse>> Handle(CreateTempShareCommand command, TeleCloudDbContext db, CurrentUserService currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);

        // Load user's plan for rate limiting
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result.Failure<TempShareResponse>(new BadRequestError("User not found."));

        var (maxBytes, maxTransfers) = PlanTierLimits.GetLimits(user.Plan);

        // Pre-check before Telegram upload to avoid wasting bandwidth
        if (maxBytes is not null || maxTransfers is not null)
        {
            var periodStart = GetCurrentPeriodStart();
            var usage = await db.UserUsages
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.PeriodStart == periodStart, ct);

            var currentBytes = usage?.TotalBytesUploaded ?? 0L;
            var currentTransfers = usage?.TransferCount ?? 0;

            if (maxBytes is not null && currentBytes + command.Content.LongLength > maxBytes.Value)
                return Result.Failure<TempShareResponse>(
                    new ForbiddenError("Monthly upload limit reached. Upgrade your plan to upload more."));

            if (maxTransfers is not null && currentTransfers + 1 > maxTransfers.Value)
                return Result.Failure<TempShareResponse>(
                    new ForbiddenError("Monthly transfer limit reached. Upgrade your plan to upload more."));
        }

        var root = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Path == "/", ct);
        if (root is null)
            return Result.Failure<TempShareResponse>(
                new BadRequestError("Root folder '/' is not initialized."));

        var channel = await telegram.GetChannelAsync(ct);

        using var stream = new MemoryStream(command.Content);
        var uploadName = command.Filename + ".bin";
        var inputFile = await telegram.Client.UploadFileAsync(stream, uploadName);

        var message = await telegram.Client.SendMediaAsync(channel.Peer, command.Filename, inputFile);

        if (message.media is not MessageMediaDocument { document: Document doc })
            return Result.Failure<TempShareResponse>(
                new LightResults.Error($"Telegram returned message media type '{message.media?.GetType().Name}' instead of document. Images/videos auto-convert to photo/video media. Use a non-image file or zip the image first."));

        var now = DateTime.UtcNow;

        // Final rate limit check using actual doc.size from Telegram
        if (maxBytes is not null || maxTransfers is not null)
        {
            var periodStart = GetCurrentPeriodStart();
            var usage = await db.UserUsages
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.PeriodStart == periodStart, ct);

            var currentBytes = usage?.TotalBytesUploaded ?? 0L;
            var currentTransfers = usage?.TransferCount ?? 0;

            if (maxBytes is not null && currentBytes + doc.size > maxBytes.Value)
                return Result.Failure<TempShareResponse>(
                    new ForbiddenError("Monthly upload limit reached. Upgrade your plan to upload more."));

            if (maxTransfers is not null && currentTransfers + 1 > maxTransfers.Value)
                return Result.Failure<TempShareResponse>(
                    new ForbiddenError("Monthly transfer limit reached. Upgrade your plan to upload more."));

            // Atomically upsert usage row — handles concurrent first-upload-of-period race
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO user_usage (id, user_id, period_start, total_bytes_uploaded, transfer_count)
                VALUES ({0}, {1}, {2}, {3}, 1)
                ON CONFLICT (user_id, period_start) DO UPDATE
                SET total_bytes_uploaded = user_usage.total_bytes_uploaded + {3},
                    transfer_count = user_usage.transfer_count + 1
                """, Guid.CreateVersion7(), userId, periodStart, doc.size, ct);
        }

        var metadata = new FileMetadata
        {
            Id = Guid.CreateVersion7(),
            Filename = command.Filename,
            ContentType = command.ContentType,
            Size = doc.size,
            ChannelId = channel.ChannelId,
            MessageId = message.ID,
            AccessHash = doc.access_hash,
            FileReference = doc.file_reference ?? [],
            DocumentId = doc.ID,
            FolderId = root.Id,
            Tags = [],
            CreatedAt = now
        };

        var share = new TempShare
        {
            Id = Guid.CreateVersion7(),
            FileId = metadata.Id,
            OwnerId = userId,
            ExpiresAt = now + command.Expiry,
            CreatedAt = now
        };

        db.Files.Add(metadata);
        db.TempShares.Add(share);
        await db.SaveChangesAsync(ct);

        var loaded = await db.TempShares
            .Include(t => t.File)
            .AsNoTracking()
            .FirstAsync(t => t.Id == share.Id, ct);

        var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        var link = $"{baseUrl}/t/{loaded.Id}";
        var downloadUrl = $"{command.SchemeHost.TrimEnd('/')}/api/temp/{loaded.Id}/download";

        return Result.Success(loaded.ToTempShareResponse(link, downloadUrl));
    }

    private static DateTime GetCurrentPeriodStart()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
