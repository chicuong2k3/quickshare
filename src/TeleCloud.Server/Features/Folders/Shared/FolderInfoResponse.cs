namespace TeleCloud.Server.Features.Folders.Shared;

public record FolderInfoResponse(
    Guid Id,
    string Path,
    DateTime CreatedAt
);
