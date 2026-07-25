namespace QuickShare.Shared.Models;

public sealed record FolderItemResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null,
    Guid? OwnerId = null,
    int FileCount = 0
);
