namespace TeleCloud.Server.Features.Files.Shared;

public record FileInfoResponse(
    Guid Id,
    string Filename,
    string ContentType,
    long Size,
    Guid FolderId,
    string FolderPath,
    string[] Tags,
    DateTime CreatedAt
);
