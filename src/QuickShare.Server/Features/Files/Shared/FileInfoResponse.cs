namespace QuickShare.Server.Features.Files.Shared;

public record FileInfoResponse(
    Guid Id,
    string Filename,
    string ContentType,
    long Size,
    Guid FolderId,
    string FolderName,
    string[] Tags,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
