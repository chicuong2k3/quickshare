using LightResults;
using TL;

namespace TeleCloud.Server.Features.Files.Upload;

public record UploadFileCommand(
    byte[] Content,
    string Filename,
    string ContentType,
    Guid FolderId,
    string[]? Tags = null
);

public sealed class UploadFileHandler(TelegramService telegram)
{
    public async Task<Result<FileInfoResponse>> Handle(UploadFileCommand command, TeleCloudDbContext db, CurrentUserService currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);

        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.FolderId, ct);
        if (folder is null)
            return Result.Failure<FileInfoResponse>(new NotFoundError("Folder", command.FolderId));

        var channel = await telegram.GetChannelAsync(ct);

        using var stream = new MemoryStream(command.Content);
        var uploadName = command.Filename + ".bin";
        var inputFile = await telegram.Client.UploadFileAsync(stream, uploadName);

        var message = await telegram.Client.SendMediaAsync(channel.Peer, command.Filename, inputFile);

        if (message.media is not MessageMediaDocument { document: Document doc })
            return Result.Failure<FileInfoResponse>(
                new LightResults.Error($"Telegram returned message media type '{message.media?.GetType().Name}' instead of document. Images/videos auto-convert to photo/video media. Use a non-image file or zip the image first."));

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
            FolderId = command.FolderId,
            OwnerId = userId,
            Tags = [],
            CreatedAt = DateTime.UtcNow
        };

        db.Files.Add(metadata);
        await db.SaveChangesAsync(ct);

        var loaded = await db.Files.Include(f => f.Folder).AsNoTracking().FirstAsync(f => f.Id == metadata.Id, ct);
        return Result.Success(loaded.ToFileInfoResponse());
    }
}
