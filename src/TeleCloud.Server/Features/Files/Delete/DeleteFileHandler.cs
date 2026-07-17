using LightResults;
using TL;

namespace TeleCloud.Server.Features.Files.Delete;

public record DeleteFileCommand(Guid Id);

public sealed class DeleteFileHandler(TelegramService telegram)
{
    public async Task<Result> Handle(DeleteFileCommand command, TeleCloudDbContext db, CancellationToken ct)
    {
        var channel = await telegram.GetChannelAsync(ct);

        var meta = await db.Files.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (meta is null)
            return Result.Failure(new NotFoundError("File", command.Id));

        await telegram.Client.Channels_DeleteMessages(channel.ToInputChannel(), [meta.MessageId]);
        db.Files.Remove(meta);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
