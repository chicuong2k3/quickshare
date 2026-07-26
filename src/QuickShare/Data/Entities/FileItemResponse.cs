namespace QuickShare.Data.Entities;

public sealed record FileItemResponse(
    Guid Id,
    string Filename,
    string ContentType,
    long Size,
    Guid FolderId,
    string FolderName,
    string[] Tags,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null
);
