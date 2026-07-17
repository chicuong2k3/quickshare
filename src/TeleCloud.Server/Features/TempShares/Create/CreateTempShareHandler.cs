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
    public async Task<Result<TempShareResponse>> Handle(CreateTempShareCommand command, TeleCloudDbContext db, CancellationToken ct)
    {
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
}
