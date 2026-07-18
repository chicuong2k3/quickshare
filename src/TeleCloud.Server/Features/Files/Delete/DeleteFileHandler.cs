using LightResults;
using TL;

namespace TeleCloud.Server.Features.Files.Delete;

public record DeleteFileCommand(Guid Id);

public sealed class DeleteFileHandler(TelegramService telegram)
{
    public async Task<Result> Handle(DeleteFileCommand command, TeleCloudDbContext db, CurrentUserService currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);
        var channel = await telegram.GetChannelAsync(ct);

        var meta = await db.Files.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (meta is null)
            return Result.Failure(new NotFoundError("File", command.Id));

        if (meta.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own this file"));

        await telegram.Client.Channels_DeleteMessages(channel.ToInputChannel(), [meta.MessageId]);
        db.Files.Remove(meta);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
